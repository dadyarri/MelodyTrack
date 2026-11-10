using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace MelodyTrack.Rewrite.Api.Tests;

// Software test authenticator: real ES256 signatures and WebAuthn wire objects, no browser/hardware claim.
internal sealed class WebAuthnTestAuthenticator : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] credentialId = RandomNumberGenerator.GetBytes(32);
    private byte[] userHandle = [];
    private string relyingParty = "localhost";
    private uint counter;

    internal string CredentialId => Encode(credentialId);

    internal string Register(string optionsJson, string origin = "https://localhost", string? challengeOverride = null)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;
        relyingParty = root.GetProperty("rp").GetProperty("id").GetString()!;
        userHandle = WebEncoders.Base64UrlDecode(root.GetProperty("user").GetProperty("id").GetString()!);
        var client = ClientData("webauthn.create", challengeOverride ?? root.GetProperty("challenge").GetString()!, origin);
        var publicKey = key.ExportParameters(false);
        var cose = new CborWriter();
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);
        cose.WriteInt32(3); cose.WriteInt32(-7);
        cose.WriteInt32(-1); cose.WriteInt32(1);
        cose.WriteInt32(-2); cose.WriteByteString(publicKey.Q.X!);
        cose.WriteInt32(-3); cose.WriteByteString(publicKey.Q.Y!);
        cose.WriteEndMap();
        var coseBytes = cose.Encode();
        var auth = new byte[37 + 16 + 2 + credentialId.Length + coseBytes.Length];
        SHA256.HashData(Encoding.UTF8.GetBytes(relyingParty)).CopyTo(auth, 0);
        auth[32] = 0x45; // User present, user verified, attested credential data.
        BinaryPrimitives.WriteUInt16BigEndian(auth.AsSpan(53, 2), checked((ushort)credentialId.Length));
        credentialId.CopyTo(auth, 55);
        coseBytes.CopyTo(auth, 55 + credentialId.Length);
        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(auth);
        attestation.WriteEndMap();
        return JsonSerializer.Serialize(new
        {
            id = CredentialId, rawId = CredentialId, type = "public-key", authenticatorAttachment = "platform",
            response = new { clientDataJSON = Encode(client), attestationObject = Encode(attestation.Encode()), transports = new[] { "internal" } },
            clientExtensionResults = new { }
        });
    }

    internal string Assert(string optionsJson, string origin = "https://localhost", string? challengeOverride = null)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var challenge = challengeOverride ?? options.RootElement.GetProperty("challenge").GetString()!;
        var client = ClientData("webauthn.get", challenge, origin);
        var auth = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(relyingParty)).CopyTo(auth, 0);
        auth[32] = 0x05; // User present and user verified.
        BinaryPrimitives.WriteUInt32BigEndian(auth.AsSpan(33), ++counter);
        var signed = new byte[auth.Length + 32];
        auth.CopyTo(signed, 0);
        SHA256.HashData(client).CopyTo(signed, auth.Length);
        var signature = key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new
        {
            id = CredentialId, rawId = CredentialId, type = "public-key", authenticatorAttachment = "platform",
            response = new { clientDataJSON = Encode(client), authenticatorData = Encode(auth), signature = Encode(signature), userHandle = Encode(userHandle) },
            clientExtensionResults = new { }
        });
    }

    private static byte[] ClientData(string type, string challenge, string origin) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });
    private static string Encode(byte[] value) => WebEncoders.Base64UrlEncode(value);
    public void Dispose() => key.Dispose();
}
