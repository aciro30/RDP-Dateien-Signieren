using System.Diagnostics;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace RdpSignTool.WinForms;

internal enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

internal sealed class RdpSigningService
{
    private const string TerminalServicesClientPolicyKeyPath = @"Software\Policies\Microsoft\Windows NT\Terminal Services\Client";
    private const string RedirectionWarningDialogVersionValueName = "RedirectionWarningDialogVersion";

    public sealed record AppConfig(string? Thumbprint, string? Subject, string? CreatedAt);
    public sealed record RdpSignatureInspection(
        bool HasSignatureField,
        bool HasSignScopeField,
        string FileHash,
        long FileSize,
        string? SignaturePreview);
    public sealed record RedirectionWarningPolicyState(
        bool ValueExists,
        int? Value,
        bool WarningSuppressed);

    public sealed record CertificateChoice(
        string Thumbprint,
        string Subject,
        DateTimeOffset NotAfter,
        string StorePath)
    {
        public override string ToString()
        {
            return $"{Subject} | {Thumbprint} | gültig bis {NotAfter:dd.MM.yyyy} | {StorePath}";
        }
    }

    public string AppDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpSignTool");

    public string ConfigPath => Path.Combine(AppDirectory, "config.json");

    public string ExportDirectory => Path.Combine(AppDirectory, "certs");

    public RdpSigningService()
    {
        Directory.CreateDirectory(AppDirectory);
        Directory.CreateDirectory(ExportDirectory);
    }

    public AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig(null, null, null);
        }

        try
        {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig(null, null, null);
        }
        catch
        {
            return new AppConfig(null, null, null);
        }
    }

    public void SaveConfig(string thumbprint, string subject)
    {
        var config = new AppConfig(thumbprint, subject, DateTimeOffset.Now.ToString("s"));
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json, Encoding.UTF8);
    }

    public string? GetRdpSignPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim(), "rdpsign.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(systemRoot))
        {
            return null;
        }

        foreach (var candidate in new[]
        {
            Path.Combine(systemRoot, "System32", "rdpsign.exe"),
            Path.Combine(systemRoot, "Sysnative", "rdpsign.exe")
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string GetRdpSignHashMode(string rdpsignPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rdpsignPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("/?");
            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            output += process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (output.Contains("/sha256", StringComparison.OrdinalIgnoreCase))
            {
                return "sha256";
            }
        }
        catch
        {
        }

        return "sha1";
    }

    public IReadOnlyList<CertificateChoice> GetExistingCodeSigningCertificates()
    {
        var result = new Dictionary<string, CertificateChoice>(StringComparer.OrdinalIgnoreCase);

        foreach (var storeTuple in new[]
        {
            (StoreName.My, StoreLocation.CurrentUser, @"CurrentUser\My"),
            (StoreName.My, StoreLocation.LocalMachine, @"LocalMachine\My")
        })
        {
            try
            {
                using var store = new X509Store(storeTuple.Item1, storeTuple.Item2);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                foreach (var cert in store.Certificates)
                {
                    if (!IsCodeSigningCandidate(cert))
                    {
                        continue;
                    }

                    var thumbprint = NormalizeThumbprint(cert.Thumbprint);
                    if (string.IsNullOrWhiteSpace(thumbprint))
                    {
                        continue;
                    }

                    if (!result.ContainsKey(thumbprint))
                    {
                        result[thumbprint] = new CertificateChoice(
                            thumbprint,
                            cert.Subject,
                            cert.NotAfter,
                            storeTuple.Item3);
                    }
                }
            }
            catch
            {
            }
        }

        return result.Values
            .OrderByDescending(c => c.NotAfter)
            .ToList();
    }

    public X509Certificate2? GetCertificateByThumbprint(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return null;
        }

        var normalized = NormalizeThumbprint(thumbprint);
        foreach (var storeTuple in new[]
        {
            (StoreName.My, StoreLocation.CurrentUser),
            (StoreName.My, StoreLocation.LocalMachine)
        })
        {
            try
            {
                using var store = new X509Store(storeTuple.Item1, storeTuple.Item2);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                foreach (var cert in store.Certificates)
                {
                    if (NormalizeThumbprint(cert.Thumbprint).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return cert;
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public X509Certificate2 CreateSelfSignedCodeSigningCertificate(string subject)
    {
        var normalizedSubject = NormalizeSubject(subject);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName(normalizedSubject),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var enhancedKeyUsage = new OidCollection();
        enhancedKeyUsage.Add(new Oid("1.3.6.1.5.5.7.3.3"));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsage, false));

        using var temporaryCertificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddMinutes(-5),
            DateTimeOffset.Now.AddYears(5));

        var password = Guid.NewGuid().ToString("N");
        var pfxBytes = temporaryCertificate.Export(X509ContentType.Pfx, password);
        using var persistedCertificate = X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            password,
            X509KeyStorageFlags.Exportable |
            X509KeyStorageFlags.PersistKeySet |
            X509KeyStorageFlags.UserKeySet,
            loaderLimits: null);

        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadWrite);
            store.Add(persistedCertificate);
        }

        return GetCertificateByThumbprint(persistedCertificate.Thumbprint)
            ?? new X509Certificate2(persistedCertificate);
    }

    public void EnsureCertificateTrust(X509Certificate2 certificate, Action<LogLevel, string>? log = null)
    {
        var thumbprint = NormalizeThumbprint(certificate.Thumbprint);
        var cerPath = Path.Combine(ExportDirectory, $"{thumbprint}.cer");
        File.WriteAllBytes(cerPath, certificate.Export(X509ContentType.Cert));
        log?.Invoke(LogLevel.Info, $"Öffentliches Zertifikat exportiert: {cerPath}");

        EnsureStoreContains(StoreName.TrustedPublisher, StoreLocation.CurrentUser, certificate, "Trusted Publishers", log);
        EnsureStoreContains(StoreName.Root, StoreLocation.CurrentUser, certificate, "Trusted Root Certification Authorities", log);
    }

    public void ExportCertificate(X509Certificate2 certificate, string destinationPath)
    {
        File.WriteAllBytes(destinationPath, certificate.Export(X509ContentType.Cert));
    }

    public RedirectionWarningPolicyState GetRedirectionWarningPolicyState()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(TerminalServicesClientPolicyKeyPath, writable: false);
        if (key is null)
        {
            return new RedirectionWarningPolicyState(false, null, false);
        }

        var rawValue = key.GetValue(RedirectionWarningDialogVersionValueName);
        if (rawValue is null)
        {
            return new RedirectionWarningPolicyState(false, null, false);
        }

        var value = Convert.ToInt32(rawValue);
        return new RedirectionWarningPolicyState(true, value, value == 1);
    }

    public void EnableRedirectionWarningSuppression()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(TerminalServicesClientPolicyKeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("Der Registry-Schlüssel konnte nicht erstellt oder geöffnet werden.");
        }

        key.SetValue(RedirectionWarningDialogVersionValueName, 1, RegistryValueKind.DWord);
    }

    public void DisableRedirectionWarningSuppression()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(TerminalServicesClientPolicyKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (key.GetValue(RedirectionWarningDialogVersionValueName) is not null)
        {
            key.DeleteValue(RedirectionWarningDialogVersionValueName, throwOnMissingValue: false);
        }
    }

    public async Task RunRdpsignAsync(
        string rdpsignPath,
        string hashMode,
        string thumbprint,
        IReadOnlyList<string> files,
        bool testOnly,
        Action<LogLevel, string> log,
        CancellationToken cancellationToken = default)
    {
        var normalizedThumbprint = NormalizeThumbprint(thumbprint);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(file))
            {
                log(LogLevel.Warning, $"Datei nicht gefunden: {file}");
                continue;
            }

            var beforeInspection = InspectRdpFile(file);

            var arguments = new List<string>
            {
                hashMode.Equals("sha256", StringComparison.OrdinalIgnoreCase) ? "/sha256" : "/sha1",
                normalizedThumbprint,
                testOnly ? "/l" : "/v",
                file
            };

            log(LogLevel.Info, $"rdpsign {BuildDisplayArguments(arguments)}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rdpsignPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    log(LogLevel.Info, e.Data.Trim());
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    log(LogLevel.Warning, e.Data.Trim());
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"rdpsign fehlgeschlagen für '{file}' mit ExitCode {process.ExitCode}.");
            }

            if (testOnly)
            {
                log(LogLevel.Success, $"Test erfolgreich: {file}");
                continue;
            }

            var afterInspection = InspectRdpFile(file);
            if (!afterInspection.HasSignatureField)
            {
                throw new InvalidOperationException(
                    $"rdpsign meldete Erfolg, aber in '{file}' wurde kein 'signature:s:'-Eintrag gefunden.");
            }

            var fileChanged = !string.Equals(beforeInspection.FileHash, afterInspection.FileHash, StringComparison.OrdinalIgnoreCase);
            if (!fileChanged && !beforeInspection.HasSignatureField)
            {
                throw new InvalidOperationException(
                    $"rdpsign meldete Erfolg, aber '{file}' wurde nicht verändert.");
            }

            log(
                LogLevel.Success,
                $"Signiert: {file}");
            log(
                LogLevel.Info,
                $"Signaturprüfung: signature:s vorhanden, signscope:s {(afterInspection.HasSignScopeField ? "vorhanden" : "nicht gefunden")}, SHA-256 {afterInspection.FileHash[..12]}...");
        }
    }

    public RdpSignatureInspection InspectRdpFile(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();

        var lines = content
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .ToArray();

        var signatureLine = lines.FirstOrDefault(line =>
            line.StartsWith("signature:s:", StringComparison.OrdinalIgnoreCase));
        var signScopeLine = lines.FirstOrDefault(line =>
            line.StartsWith("signscope:s:", StringComparison.OrdinalIgnoreCase));

        string? preview = null;
        if (!string.IsNullOrWhiteSpace(signatureLine))
        {
            var signatureValue = signatureLine["signature:s:".Length..].Trim();
            preview = signatureValue.Length <= 20
                ? signatureValue
                : $"{signatureValue[..20]}...";
        }

        var fileInfo = new FileInfo(filePath);
        using var hashStream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(hashStream));

        return new RdpSignatureInspection(
            !string.IsNullOrWhiteSpace(signatureLine),
            !string.IsNullOrWhiteSpace(signScopeLine),
            hash,
            fileInfo.Length,
            preview);
    }

    private static bool IsCodeSigningCandidate(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            return false;
        }

        if (certificate.NotAfter <= DateTime.Now)
        {
            return false;
        }

        foreach (var extension in certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>())
        {
            foreach (var usage in extension.EnhancedKeyUsages.Cast<Oid>())
            {
                if (usage.Value == "1.3.6.1.5.5.7.3.3")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeSubject(string subject)
    {
        var trimmed = string.IsNullOrWhiteSpace(subject) ? "CN=RDP File Signing" : subject.Trim();
        return trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"CN={trimmed}";
    }

    private static string NormalizeThumbprint(string? thumbprint)
    {
        return (thumbprint ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    private static string BuildDisplayArguments(IEnumerable<string> arguments)
    {
        return string.Join(
            " ",
            arguments.Select(argument =>
                argument.Contains(' ')
                    ? $"\"{argument}\""
                    : argument));
    }

    private static void EnsureStoreContains(
        StoreName storeName,
        StoreLocation storeLocation,
        X509Certificate2 certificate,
        string displayName,
        Action<LogLevel, string>? log)
    {
        var thumbprint = NormalizeThumbprint(certificate.Thumbprint);

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);

        var exists = store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .Count > 0;

        if (exists)
        {
            log?.Invoke(LogLevel.Info, $"Zertifikat ist bereits in '{displayName}' vorhanden.");
            return;
        }

        store.Add(certificate);
        log?.Invoke(LogLevel.Success, $"Zertifikat in '{displayName}' importiert.");
    }
}
