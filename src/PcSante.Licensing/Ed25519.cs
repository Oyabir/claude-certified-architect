using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace PcSante.Licensing;

/// <summary>Signature Ed25519 (BouncyCastle). La clé privée n'existe que sur le serveur de licences.</summary>
public static class Ed25519
{
    public const int KeySize = 32;
    public const int SignatureSize = 64;

    public static (byte[] PrivateKey, byte[] PublicKey) GenerateKeyPair()
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        return (privateKey.GetEncoded(), privateKey.GeneratePublicKey().GetEncoded());
    }

    public static byte[] PublicKeyFromPrivate(byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        return new Ed25519PrivateKeyParameters(privateKey, 0).GeneratePublicKey().GetEncoded();
    }

    public static byte[] Sign(byte[] privateKey, byte[] message)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(message);
        if (privateKey.Length != KeySize)
        {
            throw new ArgumentException("Clé privée Ed25519 invalide.", nameof(privateKey));
        }

        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey is not { Length: KeySize } || message is null || signature is not { Length: SignatureSize })
        {
            return false;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signature);
    }
}
