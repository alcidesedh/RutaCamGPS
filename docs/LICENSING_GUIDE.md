# Offline Cryptographic Licensing Guide — Administrator Manual

This guide explains how to manage, issue, and verify offline cryptographic licenses for **RutaCam GPS**.

---

## 1. Overview

RutaCam GPS is designed for unattended vehicle deployments where devices do not have regular internet connectivity and cannot rely on Google Play Billing.

To protect the software while allowing zero-friction offline operation, licenses are signed with **ECDSA NIST P-256 (asymmetric elliptic-curve digital signature)**.

| Key | Location | Purpose |
|---|---|---|
| **Private Key** | `tools/keygen/master_keys.json` (Local to Admin) | Signs the license payload. Kept strictly private. |
| **Public Key** | `MasterPublicKey.cs` (Embedded in App) | Verifies the cryptographic signature offline. |

---

## 2. Structure of an Activation Code

An activation code has the prefix `RCTA-` followed by hyphen-delimited 5-character blocks encoded in **Crockford Base32**:

```
RCTA-A91G2-0GA6X-5KJKA-R6983-8NSRR-C2CJT-J372N-...
```

The underlying binary payload contains:
1. **Magic Header** (`RC`, 2 bytes).
2. **Version** (`0x01`, 1 byte).
3. **Tier** (`Standard`, `Pro`, or `Enterprise`, 1 byte).
4. **Device ID** (Normalized Base32 string length + bytes).
5. **Issued Timestamp** (4-byte Unix seconds).
6. **Expiry Timestamp** (4-byte Unix seconds; `0xFFFFFFFF` for lifetime).
7. **ECDSA Signature** (64 bytes IEEE P1363: 32 bytes $r$ + 32 bytes $s$).

---

## 3. Administrator Keygen Tool (`tools/keygen`)

### 3.1 Initial Setup
To initialize your master private and public keys:

```powershell
. .\tools\env.ps1
dotnet run --project tools\keygen -- init-keys
```

This creates:
- `tools/keygen/master_keys.json`: Your private key (backed up securely).
- `src/RutaCamGPS.Core/Licensing/MasterPublicKey.cs`: Embeds the public verification key into the application.

> ⚠️ **Backup Notice**: Back up `master_keys.json` to a secure offline vault (e.g. encrypted USB drive or password manager). If you lose this key, you cannot generate licenses for existing app builds.

---

### 3.2 Generating Activation Codes for Customers

When a user taps **"Request via WhatsApp"** in the app, you will receive a message with their unique Device ID:

> *"Hello, I installed RutaCam GPS. My device ID is [7K9MX2P4W8], please send my activation code."*

Run the keygen tool with the customer's Device ID:

#### 1-Year License (Standard Sale)
```powershell
dotnet run --project tools\keygen -- generate --device 7K9MX2P4W8 --days 365
```

#### Permanent (Lifetime) License
```powershell
dotnet run --project tools\keygen -- generate --device 7K9MX2P4W8 --lifetime
```

#### 30-Day Evaluation / Trial
```powershell
dotnet run --project tools\keygen -- generate --device 7K9MX2P4W8 --days 30
```

#### Enterprise Tier
```powershell
dotnet run --project tools\keygen -- generate --device 7K9MX2P4W8 --tier Enterprise --days 365
```

The CLI outputs a formatted response ready to send back to the user via WhatsApp:

```
==========================================================
                RUTACAM GPS LICENSE GENERATED             
==========================================================
Device ID : 7K9MX2P4W8
Tier      : Pro
Issued    : 09/10/2026 11:14
Expires   : 09/10/2027 11:14

ACTIVATION CODE:
RCTA-A91G2-0GA6X-5KJKA-R6983-8NSRR-C2CJT-J372N-6STG9-G6HRC-STFNZ-1CTPR-6B30F-PTDG1-EPFQ4-FYBWN-Z36WN-W1DK3-RSXN2-RQMZN-KFR58-N4CSH-165F8-87QZZ-WTV69-AN23G-DT7XT-68KC2-9MTB0

WhatsApp Message:
----------------------------------------------------------
Hello! Your license for RutaCam GPS is ready.

Activation Code:
RCTA-A91G2-0GA6X-5KJKA-R6983-8NSRR-C2CJT-J372N-6STG9-G6HRC-STFNZ-1CTPR-6B30F-PTDG1-EPFQ4-FYBWN-Z36WN-W1DK3-RSXN2-RQMZN-KFR58-N4CSH-165F8-87QZZ-WTV69-AN23G-DT7XT-68KC2-9MTB0

Duration: 365 days (until 09/10/2027)

Enter this code in Settings > License > Enter Code.
----------------------------------------------------------
```

---

### 3.3 Verifying an Existing Code

To check whether a given code is valid or has expired:

```powershell
dotnet run --project tools\keygen -- verify --device 7K9MX2P4W8 --code RCTA-XXXXX-...
```

---

## 4. Troubleshooting Activation Issues

| Issue | Cause | Resolution |
|---|---|---|
| *"Este código fue emitido para otro dispositivo"* | Customer entered a code generated for a different phone. | Check the customer's actual Device ID displayed in their settings screen and re-generate. |
| *"La licencia expiró..."* | The license validity window has elapsed. | Issue a renewed license code. |
| *"La firma criptográfica no es válida..."* | Code was partially truncated, copied incompletely, or altered. | Re-send the full code and ask the user to use the clipboard copy button. |
| *"Formato no válido"* | Non-alphanumeric characters or unrecognized prefix. | Ensure the code begins with `RCTA-`. |
