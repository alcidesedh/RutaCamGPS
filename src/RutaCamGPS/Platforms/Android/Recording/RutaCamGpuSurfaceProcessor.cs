using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Android.Graphics;
using Android.Opengl;
using Android.Runtime;
using Android.Util;
using Android.Views;
using AndroidX.Camera.Core;
using AndroidX.Core.Util;
using Java.Interop;
using Java.Lang;
using Java.Nio;
using Java.Util.Concurrent;

namespace GPSCamRoute.Platforms.Android.Recording;

public sealed class RutaCamGpuSurfaceProcessor : Java.Lang.Object, ISurfaceProcessor, IJavaObject, IDisposable, IJavaPeerable
{
	private sealed class FrameListener(RutaCamGpuSurfaceProcessor owner, InputTarget input) : Java.Lang.Object(), SurfaceTexture.IOnFrameAvailableListener, IJavaObject, IDisposable, IJavaPeerable
	{
		public void OnFrameAvailable(SurfaceTexture? surfaceTexture)
		{
			owner.QueueRenderFrame(input);
		}

	}

	private sealed class InputSurfaceResultConsumer(RutaCamGpuSurfaceProcessor owner, InputTarget input) : Java.Lang.Object(), IConsumer, IJavaObject, IDisposable, IJavaPeerable
	{
		public void Accept(Java.Lang.Object? result)
		{
			owner.ReleaseInput(input);
		}

	}

	private sealed class OutputEventConsumer(RutaCamGpuSurfaceProcessor owner, ISurfaceOutput output) : Java.Lang.Object(), IConsumer, IJavaObject, IDisposable, IJavaPeerable
	{
		public void Accept(Java.Lang.Object? value)
		{
			owner.CloseOutput(output);
		}

	}

	private sealed class InputTarget(int generation, SurfaceRequest request, int textureId, SurfaceTexture surfaceTexture, Surface surface)
	{
		public int Generation { get; } = generation;

		public SurfaceRequest Request { get; } = request;

		public int TextureId { get; } = textureId;

		public SurfaceTexture SurfaceTexture { get; } = surfaceTexture;

		public Surface Surface { get; } = surface;

		public bool Released { get; set; }
	}

	private sealed class OutputTarget(ISurfaceOutput surfaceOutput, Surface surface, EGLSurface eglSurface, int width, int height, int targets)
	{
		public ISurfaceOutput SurfaceOutput { get; } = surfaceOutput;

		public Surface Surface { get; } = surface;

		public EGLSurface EglSurface { get; } = eglSurface;

		public int Width { get; } = width;

		public int Height { get; } = height;

		public int Targets { get; } = targets;

		public bool Closed { get; set; }
	}

	private const string VertexShader = "attribute vec4 aPosition;\nattribute vec4 aTexCoord;\nuniform mat4 uTexMatrix;\nuniform mat4 uPositionMatrix;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_Position = uPositionMatrix * aPosition;\n    vTexCoord = (uTexMatrix * aTexCoord).xy;\n}";

	private const string FragmentShader = "#extension GL_OES_EGL_image_external : require\nprecision mediump float;\nuniform samplerExternalOES uTexture;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_FragColor = texture2D(uTexture, vTexCoord);\n}";

	private const string HudVertexShader = "attribute vec4 aPosition;\nattribute vec4 aTexCoord;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_Position = aPosition;\n    vTexCoord = vec2(aTexCoord.x, 1.0 - aTexCoord.y);\n}";

	private const string HudFragmentShader = "precision mediump float;\nuniform sampler2D uHudTexture;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_FragColor = texture2D(uHudTexture, vTexCoord);\n}";

	private const int VideoCaptureTarget = 2;

	private const long HudRefreshIntervalNs = 100000000L;

	private readonly object _sync = new object();

	private readonly RutaCamGpuMotionTracker _motion;

	private readonly IExecutor _executor;

	private readonly Func<int, int, int, Bitmap?>? _hudBitmapProvider;

	private EGLDisplay? _eglDisplay;

	private EGLContext? _eglContext;

	private EGLConfig? _eglConfig;

	private EGLSurface? _eglPbufferSurface;

	private readonly List<InputTarget> _inputs = new List<InputTarget>();

	private InputTarget? _activeInput;

	private InputTarget? _pendingInput;

	private int _nextInputGeneration;

	private int _program;

	private int _aPosition;

	private int _aTexCoord;

	private int _uTexMatrix;

	private int _uPositionMatrix;

	private int _hudProgram;

	private int _hudTexture;

	private int _hudAPosition;

	private int _hudATexCoord;

	private int _hudUTexture;

	private long _lastHudUploadNs;

	private int _hudOutputWidth;

	private int _hudOutputHeight;

	private int _hudOutputRotationDegrees = -1;

	private bool _hudTextureReady;

	private readonly float[] _surfaceTextureMatrix = new float[16];

	private readonly float[] _cameraTransformMatrix = new float[16];

	private readonly float[] _positionMatrix = new float[16];

	private readonly List<OutputTarget> _outputs = new List<OutputTarget>();

	private int _renderQueued;

	private long _frameSignalCount;

	private long _renderCount;

	private int _consecutiveRenderErrors;

	private long _lastFrameTimestampNs;

	private long _frameGapOver50Count;

	private double _maxFrameGapMs;

	private bool _released;

	private bool _disposed;

	private static readonly float[] Quad = new float[16]
	{
		-1f, -1f, 0f, 1f, 1f, -1f, 0f, 1f, -1f, 1f,
		0f, 1f, 1f, 1f, 0f, 1f
	};

	private static readonly float[] Tex = new float[16]
	{
		0f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 0f, 1f,
		0f, 1f, 1f, 1f, 0f, 1f
	};

	private readonly FloatBuffer _quadBuffer = CreateFloatBuffer(Quad);

	private readonly FloatBuffer _texBuffer = CreateFloatBuffer(Tex);

	public RutaCamGpuSurfaceProcessor(RutaCamGpuMotionTracker motion, IExecutor executor, Func<int, int, int, Bitmap?>? hudBitmapProvider = null)
	{
		_motion = motion;
		_executor = executor;
		_hudBitmapProvider = hudBitmapProvider;
	}

	public void OnInputSurface(SurfaceRequest request)
	{
		EnsureGl();
		MakeParkingContextCurrent();
		Size resolution = request.Resolution;
		int textureId = CreateExternalTexture();
		SurfaceTexture surfaceTexture = new SurfaceTexture(textureId);
		surfaceTexture.SetDefaultBufferSize(resolution.Width, resolution.Height);
		Surface surface = new Surface(surfaceTexture);
		int generation = Interlocked.Increment(ref _nextInputGeneration);
		InputTarget input = new InputTarget(generation, request, textureId, surfaceTexture, surface);
		surfaceTexture.SetOnFrameAvailableListener(new FrameListener(this, input));
		lock (_sync)
		{
			_inputs.Add(input);
			_activeInput = input;
			_pendingInput = null;
			_lastFrameTimestampNs = 0L;
			_frameGapOver50Count = 0L;
			_maxFrameGapMs = 0.0;
		}
		Debug.WriteLine($"RutaCam GPU OnInputSurface · gen={generation} · {resolution.Width}x{resolution.Height}");
		try
		{
			request.ProvideSurface(surface, _executor, new InputSurfaceResultConsumer(this, input));
		}
		catch
		{
			ReleaseInput(input);
			throw;
		}
	}

	public void OnOutputSurface(ISurfaceOutput surfaceOutput)
	{
		EnsureGl();
		Surface surface = surfaceOutput.GetSurface(_executor, new OutputEventConsumer(this, surfaceOutput));
		if (surface == null)
		{
			return;
		}
		EGLSurface eglSurface = EGL14.EglCreateWindowSurface(_eglDisplay, _eglConfig, surface, new int[1] { 12344 }, 0);
		if (eglSurface == null || eglSurface == EGL14.EglNoSurface)
		{
			surfaceOutput.Close();
			surface.Release();
			return;
		}
		Debug.WriteLine($"RutaCam GPU OnOutputSurface · targets={surfaceOutput.Targets} · size={surfaceOutput.Size.Width}x{surfaceOutput.Size.Height}");
		lock (_sync)
		{
			_outputs.Add(new OutputTarget(surfaceOutput, surface, eglSurface, surfaceOutput.Size.Width, surfaceOutput.Size.Height, surfaceOutput.Targets));
		}
	}

	private void QueueRenderFrame(InputTarget input)
	{
		if (_released || input.Released)
		{
			return;
		}
		Interlocked.Increment(ref _frameSignalCount);
		lock (_sync)
		{
			if (_released || input.Released || _activeInput != input)
			{
				return;
			}
			_pendingInput = input;
			if (_renderQueued != 0)
			{
				return;
			}
			_renderQueued = 1;
		}
		try
		{
			_executor.Execute(new Runnable(ProcessRenderQueue));
		}
		catch (System.Exception value)
		{
			lock (_sync)
			{
				_renderQueued = 0;
				_pendingInput = null;
			}
			Debug.WriteLine($"RutaCam GPU QueueRenderFrame ERROR: {value}");
		}
	}

	private void ProcessRenderQueue()
	{
		while (!_released)
		{
			InputTarget input;
			lock (_sync)
			{
				input = _pendingInput;
				_pendingInput = null;
				if (input == null)
				{
					_renderQueued = 0;
					return;
				}
			}
			if (!input.Released)
			{
				RenderFrame(input);
			}
		}
		lock (_sync)
		{
			_renderQueued = 0;
			_pendingInput = null;
		}
	}

	private void RenderFrame(InputTarget input)
	{
		if (_released || input.Released || _eglDisplay == null || _eglContext == null)
		{
			return;
		}
		try
		{
			MakeParkingContextCurrent();
			input.SurfaceTexture.UpdateTexImage();
			Interlocked.Increment(ref _renderCount);
			long timestampNs = input.SurfaceTexture.Timestamp;
			UpdateFrameTiming(timestampNs);
			input.SurfaceTexture.GetTransformMatrix(_surfaceTextureMatrix);
			OutputTarget[] outputs;
			lock (_sync)
			{
				outputs = _outputs.ToArray();
			}
			RutaCamGpuMotionTracker.MotionSnapshot motion = _motion.Snapshot;
			BuildPositionMatrix(motion, _positionMatrix);
			OutputTarget[] array = outputs;
			foreach (OutputTarget output in array)
			{
				if (!output.Closed && EGL14.EglMakeCurrent(_eglDisplay, output.EglSurface, output.EglSurface, _eglContext))
				{
					GLES20.GlViewport(0, 0, output.Width, output.Height);
					GLES20.GlClearColor(0f, 0f, 0f, 1f);
					GLES20.GlClear(16384);
					GLES20.GlUseProgram(_program);
					Array.Copy(_surfaceTextureMatrix, _cameraTransformMatrix, 16);
					try
					{
						output.SurfaceOutput.UpdateTransformMatrix(_cameraTransformMatrix, _surfaceTextureMatrix);
					}
					catch
					{
						Array.Copy(_surfaceTextureMatrix, _cameraTransformMatrix, 16);
					}
					GLES20.GlUniformMatrix4fv(_uTexMatrix, 1, transpose: false, _cameraTransformMatrix, 0);
					GLES20.GlUniformMatrix4fv(_uPositionMatrix, 1, transpose: false, _positionMatrix, 0);
					_quadBuffer.Position(0);
					GLES20.GlEnableVertexAttribArray(_aPosition);
					GLES20.GlVertexAttribPointer(_aPosition, 4, 5126, normalized: false, 16, _quadBuffer);
					_texBuffer.Position(0);
					GLES20.GlEnableVertexAttribArray(_aTexCoord);
					GLES20.GlVertexAttribPointer(_aTexCoord, 4, 5126, normalized: false, 16, _texBuffer);
					GLES20.GlActiveTexture(33984);
					GLES20.GlBindTexture(36197, input.TextureId);
					GLES20.GlDrawArrays(5, 0, 4);
					GLES20.GlDisableVertexAttribArray(_aPosition);
					GLES20.GlDisableVertexAttribArray(_aTexCoord);
					if ((output.Targets & 2) != 0)
					{
						RenderHud(output, timestampNs);
					}
					try
					{
						EGLExt.EglPresentationTimeANDROID(_eglDisplay, output.EglSurface, timestampNs);
					}
					catch
					{
					}
					EGL14.EglSwapBuffers(_eglDisplay, output.EglSurface);
				}
			}
			MakeParkingContextCurrent();
			_consecutiveRenderErrors = 0;
			if (_renderCount % 300 == 0)
			{
				ComputeCompensation(motion, out var calTx, out var calTy, out var calRollDegrees);
				string saturation = (motion.PitchSaturated ? "P" : "-") + (motion.YawSaturated ? "Y" : "-") + (motion.RollSaturated ? "R" : "-");
				Debug.WriteLine($"RutaCam GPU frames · signals={_frameSignalCount} · renders={_renderCount} · outputs={outputs.Length} · gap>50={_frameGapOver50Count} · maxGap={_maxFrameGapMs:F1}ms");
				Debug.WriteLine($"RutaCam GPU CAL · profile={motion.ProfileName} · rot={motion.TargetRotation} · cam={(motion.UseFrontCamera ? "FRONT" : "BACK")} · P={motion.PitchDegrees:+0.00;-0.00;0.00}° Y={motion.YawDegrees:+0.00;-0.00;0.00}° R={motion.RollDegrees:+0.00;-0.00;0.00}° · TX={calTx:+0.000;-0.000;0.000} TY={calTy:+0.000;-0.000;0.000} RZ={calRollDegrees:+0.00;-0.00;0.00}° · gyro={motion.GyroX:+0.000;-0.000;0.000}/{motion.GyroY:+0.000;-0.000;0.000}/{motion.GyroZ:+0.000;-0.000;0.000} · vib={motion.VibrationG:F3}g · sat={saturation} · zoom={motion.Zoom:F2}x");
			}
		}
		catch (System.Exception value)
		{
			int errors = Interlocked.Increment(ref _consecutiveRenderErrors);
			Debug.WriteLine($"RutaCam GPU RenderFrame ERROR gen={input.Generation} count={errors}: {value}");
			if (errors >= 3 && !input.Released)
			{
				try
				{
					bool invalidated = input.Request.Invalidate();
					Debug.WriteLine($"RutaCam GPU input invalidate gen={input.Generation} · result={invalidated}");
				}
				catch (System.Exception value2)
				{
					Debug.WriteLine($"RutaCam GPU invalidate ERROR: {value2}");
				}
				Interlocked.Exchange(ref _consecutiveRenderErrors, 0);
			}
		}
	}

	private void RenderHud(OutputTarget output, long timestampNs)
	{
		if (_hudBitmapProvider == null || _hudProgram == 0)
		{
			return;
		}
		bool sizeChanged = _hudOutputWidth != output.Width || _hudOutputHeight != output.Height;
		bool orientationChanged = _hudOutputRotationDegrees != 0;
		if (!_hudTextureReady || sizeChanged || orientationChanged || timestampNs <= 0 || _lastHudUploadNs <= 0 || timestampNs - _lastHudUploadNs >= 100000000)
		{
			try
			{
				Bitmap bitmap = _hudBitmapProvider(output.Width, output.Height, 0);
				if (bitmap != null && !bitmap.IsRecycled)
				{
					EnsureHudTexture();
					GLES20.GlActiveTexture(33984);
					GLES20.GlBindTexture(3553, _hudTexture);
					GLUtils.TexImage2D(3553, 0, bitmap, 0);
					_hudTextureReady = true;
					if (_hudOutputRotationDegrees != 0)
					{
						Debug.WriteLine($"RutaCam GPU HUD geometry · targets={output.Targets} · surface={output.Width}x{output.Height} · rotation=0° (output-space)");
					}
					_hudOutputWidth = output.Width;
					_hudOutputHeight = output.Height;
					_hudOutputRotationDegrees = 0;
					_lastHudUploadNs = timestampNs;
				}
			}
			catch (System.Exception value)
			{
				Debug.WriteLine($"RutaCam GPU HUD upload ERROR: {value}");
				return;
			}
		}
		if (_hudTextureReady && _hudTexture != 0)
		{
			GLES20.GlEnable(3042);
			GLES20.GlBlendFunc(1, 771);
			GLES20.GlUseProgram(_hudProgram);
			_quadBuffer.Position(0);
			GLES20.GlEnableVertexAttribArray(_hudAPosition);
			GLES20.GlVertexAttribPointer(_hudAPosition, 4, 5126, normalized: false, 16, _quadBuffer);
			_texBuffer.Position(0);
			GLES20.GlEnableVertexAttribArray(_hudATexCoord);
			GLES20.GlVertexAttribPointer(_hudATexCoord, 4, 5126, normalized: false, 16, _texBuffer);
			GLES20.GlActiveTexture(33984);
			GLES20.GlBindTexture(3553, _hudTexture);
			GLES20.GlUniform1i(_hudUTexture, 0);
			GLES20.GlDrawArrays(5, 0, 4);
			GLES20.GlDisableVertexAttribArray(_hudAPosition);
			GLES20.GlDisableVertexAttribArray(_hudATexCoord);
			GLES20.GlDisable(3042);
		}
	}

	private static int TargetRotationToDegrees(int targetRotation)
	{
		if (1 == 0)
		{
		}
		int result = targetRotation switch
		{
			1 => 90, 
			2 => 180, 
			3 => 270, 
			_ => 0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private void EnsureHudTexture()
	{
		if (_hudTexture == 0)
		{
			int[] ids = new int[1];
			GLES20.GlGenTextures(1, ids, 0);
			_hudTexture = ids[0];
			GLES20.GlBindTexture(3553, _hudTexture);
			GLES20.GlTexParameteri(3553, 10241, 9729);
			GLES20.GlTexParameteri(3553, 10240, 9729);
			GLES20.GlTexParameteri(3553, 10242, 33071);
			GLES20.GlTexParameteri(3553, 10243, 33071);
		}
	}

	private static void BuildPositionMatrix(RutaCamGpuMotionTracker.MotionSnapshot motion, float[] matrix)
	{
		global::Android.Opengl.Matrix.SetIdentityM(matrix, 0);
		global::Android.Opengl.Matrix.ScaleM(matrix, 0, (float)motion.Zoom, (float)motion.Zoom, 1f);
		ComputeCompensation(motion, out var tx, out var ty, out var rollDegrees);
		global::Android.Opengl.Matrix.TranslateM(matrix, 0, tx, ty, 0f);
		global::Android.Opengl.Matrix.RotateM(matrix, 0, rollDegrees, 0f, 0f, 1f);
	}

	private static void ComputeCompensation(RutaCamGpuMotionTracker.MotionSnapshot motion, out float tx, out float ty, out float rollDegrees)
	{
		double horizontal = motion.PitchNormalized;
		double vertical = motion.YawNormalized;
		double rollRadians = motion.RollRadians;
		double absHorizontal = System.Math.Abs(horizontal);
		double absVertical = System.Math.Abs(vertical);
		bool horizontalDominant = absHorizontal >= 0.07 && absHorizontal >= absVertical * 1.35;
		bool verticalDominant = absVertical >= 0.07 && absVertical >= absHorizontal * 1.35;
		if (horizontalDominant)
		{
			vertical = 0.0;
		}
		else if (verticalDominant)
		{
			horizontal = 0.0;
		}
		double absPitchDeg = System.Math.Abs(motion.PitchDegrees);
		double absYawDeg = System.Math.Abs(motion.YawDegrees);
		double absRollDeg = System.Math.Abs(motion.RollDegrees);
		double planarDeg = System.Math.Max(absPitchDeg, absYawDeg);
		double rollWeight;
		if (absRollDeg <= 0.12)
		{
			rollWeight = 0.0;
		}
		else if (planarDeg <= 0.18)
		{
			rollWeight = 1.0;
		}
		else
		{
			double ratio = absRollDeg / (planarDeg + 0.001);
			double t = System.Math.Clamp((ratio - 0.42) / 0.48000000000000004, 0.0, 1.0);
			rollWeight = t * t * (3.0 - 2.0 * t);
		}
		if ((horizontalDominant || verticalDominant) && rollWeight < 0.85)
		{
			rollWeight *= 0.35;
		}
		rollRadians *= rollWeight;
		tx = (float)(motion.VerticalSign * horizontal * motion.TranslationMargin);
		ty = (float)(motion.HorizontalSign * vertical * motion.TranslationMargin);
		rollDegrees = (float)(motion.RollSign * rollRadians * 180.0 / System.Math.PI * motion.RollStrength);
	}

	private void UpdateFrameTiming(long timestampNs)
	{
		if (timestampNs <= 0)
		{
			return;
		}
		long previous = _lastFrameTimestampNs;
		_lastFrameTimestampNs = timestampNs;
		if (previous > 0 && timestampNs > previous)
		{
			double gapMs = (double)(timestampNs - previous) / 1000000.0;
			if (gapMs > _maxFrameGapMs)
			{
				_maxFrameGapMs = gapMs;
			}
			if (gapMs > 50.0)
			{
				Interlocked.Increment(ref _frameGapOver50Count);
			}
		}
	}

	private void EnsureGl()
	{
		if (_eglDisplay == null || _eglContext == null || _program == 0)
		{
			_eglDisplay = EGL14.EglGetDisplay(0);
			if (_eglDisplay == null || _eglDisplay == EGL14.EglNoDisplay)
			{
				throw new InvalidOperationException("EGL display no disponible.");
			}
			int[] versions = new int[2];
			if (!EGL14.EglInitialize(_eglDisplay, versions, 0, versions, 1))
			{
				throw new InvalidOperationException("No se pudo inicializar EGL.");
			}
			EGLConfig[] configs = new EGLConfig[1];
			int[] numConfigs = new int[1];
			int[] attributes = new int[11]
			{
				12324, 8, 12323, 8, 12322, 8, 12321, 8, 12352, 4,
				12344
			};
			if (!EGL14.EglChooseConfig(_eglDisplay, attributes, 0, configs, 0, configs.Length, numConfigs, 0) || numConfigs[0] <= 0)
			{
				throw new InvalidOperationException("No se encontró EGLConfig.");
			}
			_eglConfig = configs[0];
			int[] contextAttribs = new int[3] { 12440, 2, 12344 };
			_eglContext = EGL14.EglCreateContext(_eglDisplay, _eglConfig, EGL14.EglNoContext, contextAttribs, 0);
			if (_eglContext == null || _eglContext == EGL14.EglNoContext)
			{
				throw new InvalidOperationException("No se pudo crear contexto OpenGL ES 2.");
			}
			int[] tinyAttribs = new int[5] { 12375, 1, 12374, 1, 12344 };
			_eglPbufferSurface = EGL14.EglCreatePbufferSurface(_eglDisplay, _eglConfig, tinyAttribs, 0);
			if (_eglPbufferSurface == null || _eglPbufferSurface == EGL14.EglNoSurface || !EGL14.EglMakeCurrent(_eglDisplay, _eglPbufferSurface, _eglPbufferSurface, _eglContext))
			{
				throw new InvalidOperationException("No se pudo activar contexto OpenGL.");
			}
			_program = BuildProgram("attribute vec4 aPosition;\nattribute vec4 aTexCoord;\nuniform mat4 uTexMatrix;\nuniform mat4 uPositionMatrix;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_Position = uPositionMatrix * aPosition;\n    vTexCoord = (uTexMatrix * aTexCoord).xy;\n}", "#extension GL_OES_EGL_image_external : require\nprecision mediump float;\nuniform samplerExternalOES uTexture;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_FragColor = texture2D(uTexture, vTexCoord);\n}");
			_aPosition = GLES20.GlGetAttribLocation(_program, "aPosition");
			_aTexCoord = GLES20.GlGetAttribLocation(_program, "aTexCoord");
			_uTexMatrix = GLES20.GlGetUniformLocation(_program, "uTexMatrix");
			_uPositionMatrix = GLES20.GlGetUniformLocation(_program, "uPositionMatrix");
			_hudProgram = BuildProgram("attribute vec4 aPosition;\nattribute vec4 aTexCoord;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_Position = aPosition;\n    vTexCoord = vec2(aTexCoord.x, 1.0 - aTexCoord.y);\n}", "precision mediump float;\nuniform sampler2D uHudTexture;\nvarying vec2 vTexCoord;\nvoid main() {\n    gl_FragColor = texture2D(uHudTexture, vTexCoord);\n}");
			_hudAPosition = GLES20.GlGetAttribLocation(_hudProgram, "aPosition");
			_hudATexCoord = GLES20.GlGetAttribLocation(_hudProgram, "aTexCoord");
			_hudUTexture = GLES20.GlGetUniformLocation(_hudProgram, "uHudTexture");
		}
	}

	private void MakeParkingContextCurrent()
	{
		if (_eglDisplay == null || _eglContext == null || _eglPbufferSurface == null || _eglPbufferSurface == EGL14.EglNoSurface)
		{
			throw new InvalidOperationException("Contexto EGL de RutaCam GPU no inicializado.");
		}
		if (!EGL14.EglMakeCurrent(_eglDisplay, _eglPbufferSurface, _eglPbufferSurface, _eglContext))
		{
			throw new InvalidOperationException($"EGL no pudo activar contexto parking. Error=0x{EGL14.EglGetError():X}");
		}
	}

	private static int BuildProgram(string vertexSource, string fragmentSource)
	{
		int vertex = CompileShader(35633, vertexSource);
		int fragment = CompileShader(35632, fragmentSource);
		int program = GLES20.GlCreateProgram();
		GLES20.GlAttachShader(program, vertex);
		GLES20.GlAttachShader(program, fragment);
		GLES20.GlLinkProgram(program);
		int[] status = new int[1];
		GLES20.GlGetProgramiv(program, 35714, status, 0);
		GLES20.GlDeleteShader(vertex);
		GLES20.GlDeleteShader(fragment);
		if (status[0] == 0)
		{
			string log = GLES20.GlGetProgramInfoLog(program);
			GLES20.GlDeleteProgram(program);
			throw new InvalidOperationException("Error enlazando shader GPU: " + log);
		}
		return program;
	}

	private static int CompileShader(int type, string source)
	{
		int shader = GLES20.GlCreateShader(type);
		GLES20.GlShaderSource(shader, source);
		GLES20.GlCompileShader(shader);
		int[] status = new int[1];
		GLES20.GlGetShaderiv(shader, 35713, status, 0);
		if (status[0] == 0)
		{
			string log = GLES20.GlGetShaderInfoLog(shader);
			GLES20.GlDeleteShader(shader);
			throw new InvalidOperationException("Error compilando shader GPU: " + log);
		}
		return shader;
	}

	private static int CreateExternalTexture()
	{
		int[] ids = new int[1];
		GLES20.GlGenTextures(1, ids, 0);
		int id = ids[0];
		GLES20.GlBindTexture(36197, id);
		GLES20.GlTexParameteri(36197, 10241, 9729);
		GLES20.GlTexParameteri(36197, 10240, 9729);
		GLES20.GlTexParameteri(36197, 10242, 33071);
		GLES20.GlTexParameteri(36197, 10243, 33071);
		return id;
	}

	private static FloatBuffer CreateFloatBuffer(float[] values)
	{
		ByteBuffer byteBuffer = ByteBuffer.AllocateDirect(values.Length * 4).Order(ByteOrder.NativeOrder());
		FloatBuffer floatBuffer = byteBuffer.AsFloatBuffer();
		floatBuffer.Put(values);
		floatBuffer.Position(0);
		return floatBuffer;
	}

	private void CloseOutput(ISurfaceOutput output)
	{
		OutputTarget target = null;
		lock (_sync)
		{
			target = _outputs.FirstOrDefault((OutputTarget x) => x.SurfaceOutput == output);
			if (target != null)
			{
				target.Closed = true;
				_outputs.Remove(target);
			}
		}
		if (target == null)
		{
			return;
		}
		try
		{
			if (_eglDisplay != null && target.EglSurface != null)
			{
				EGL14.EglDestroySurface(_eglDisplay, target.EglSurface);
			}
		}
		catch
		{
		}
		try
		{
			target.SurfaceOutput.Close();
		}
		catch
		{
		}
	}

	private void ReleaseInput(InputTarget input)
	{
		if (input.Released)
		{
			return;
		}
		input.Released = true;
		lock (_sync)
		{
			_inputs.Remove(input);
			if (_activeInput == input)
			{
				_activeInput = null;
			}
			if (_pendingInput == input)
			{
				_pendingInput = null;
			}
		}
		try
		{
			input.SurfaceTexture.SetOnFrameAvailableListener(null);
		}
		catch
		{
		}
		try
		{
			if (_eglDisplay != null && _eglContext != null && _eglPbufferSurface != null && _eglPbufferSurface != EGL14.EglNoSurface)
			{
				MakeParkingContextCurrent();
				if (input.TextureId != 0)
				{
					GLES20.GlDeleteTextures(1, new int[1] { input.TextureId }, 0);
				}
			}
		}
		catch
		{
		}
		try
		{
			input.Surface.Release();
		}
		catch
		{
		}
		try
		{
			input.SurfaceTexture.Release();
		}
		catch
		{
		}
		Debug.WriteLine($"RutaCam GPU ReleaseInput · gen={input.Generation}");
	}

	public void Release()
	{
		if (_released)
		{
			return;
		}
		_released = true;
		Volatile.Write(ref _renderQueued, 0);
		InputTarget[] inputs;
		OutputTarget[] outputs;
		lock (_sync)
		{
			inputs = _inputs.ToArray();
			outputs = _outputs.ToArray();
			_outputs.Clear();
			_activeInput = null;
			_pendingInput = null;
		}
		InputTarget[] array = inputs;
		foreach (InputTarget input in array)
		{
			ReleaseInput(input);
		}
		OutputTarget[] array2 = outputs;
		foreach (OutputTarget output in array2)
		{
			try
			{
				if (_eglDisplay != null)
				{
					EGL14.EglDestroySurface(_eglDisplay, output.EglSurface);
				}
			}
			catch
			{
			}
			try
			{
				output.SurfaceOutput.Close();
			}
			catch
			{
			}
		}
		try
		{
			if (_eglDisplay != null && _eglContext != null && _eglPbufferSurface != null && _eglPbufferSurface != EGL14.EglNoSurface)
			{
				EGL14.EglMakeCurrent(_eglDisplay, _eglPbufferSurface, _eglPbufferSurface, _eglContext);
				if (_program != 0)
				{
					GLES20.GlDeleteProgram(_program);
					_program = 0;
				}
				if (_hudProgram != 0)
				{
					GLES20.GlDeleteProgram(_hudProgram);
					_hudProgram = 0;
				}
				if (_hudTexture != 0)
				{
					GLES20.GlDeleteTextures(1, new int[1] { _hudTexture }, 0);
					_hudTexture = 0;
					_hudTextureReady = false;
				}
				EGL14.EglMakeCurrent(_eglDisplay, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
				EGL14.EglDestroySurface(_eglDisplay, _eglPbufferSurface);
				EGL14.EglDestroyContext(_eglDisplay, _eglContext);
				EGL14.EglTerminate(_eglDisplay);
			}
		}
		catch
		{
		}
		_eglPbufferSurface = null;
		_eglContext = null;
		_eglDisplay = null;
		_eglConfig = null;
	}

	public new void Dispose()
	{
		if (!_disposed)
		{
			Release();
			_quadBuffer.Dispose();
			_texBuffer.Dispose();
			_disposed = true;
		}
	}

}
