using System.Security.Cryptography;

// Release signing for Shazim updates.
//   keygen <private-key.pem>   creates a new ECDSA P-256 key and prints the public key to embed in the app
//   pubkey <private-key.pem>   prints the public key again
//   sign   <private-key.pem> <file>   writes <file>.sig (base64, IEEE P1363, SHA-256)
// The private key never goes into the repository: keep it (and a backup) on your own machine.

if (args.Length < 2) return Usage();
var keyPath = args[1];

switch (args[0])
{
    case "keygen":
        if (File.Exists(keyPath)) { Console.Error.WriteLine($"Refusing to overwrite {keyPath}."); return 2; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
        using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
            Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        }
        return 0;

    case "pubkey":
        using (var ecdsa = Load(keyPath)) Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;

    case "sign" when args.Length >= 3:
        using (var ecdsa = Load(keyPath))
        {
            var data = File.ReadAllBytes(args[2]);
            var sig = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            File.WriteAllText(args[2] + ".sig", Convert.ToBase64String(sig) + "\n");
            // Check it straight away with the public key, like the app will.
            using var pub = ECDsa.Create();
            pub.ImportSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out _);
            if (!pub.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return 3;
        }
        return 0;

    default:
        return Usage();
}

static ECDsa Load(string path)
{
    var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(path));
    return ecdsa;
}

static int Usage()
{
    Console.Error.WriteLine("usage: ReleaseSigner keygen|pubkey <key.pem> | sign <key.pem> <file>");
    return 1;
}
