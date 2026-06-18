using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Logging;
using MorningCat.Config;
using MorningCat.I18n;

namespace MorningCat.Security
{
    public class PluginSignatureVerifier
    {
        private const string SIGNATURE_URL = "https://110.42.98.47:59113/api/preview/MorningCat%2FPluginSignature.txt";

        private readonly ConfigManager _configManager;
        private readonly bool _testMode;
        private string _publicKey;

        public bool IsTestMode => _testMode;
        public string PublicKey => _publicKey;

        public PluginSignatureVerifier(ConfigManager configManager, bool testMode = false)
        {
            _configManager = configManager;
            _testMode = testMode;
            _publicKey = configManager.GetConfig().PluginSignaturePublicKey;
        }

        public async Task FetchPublicKeyAsync()
        {
            try
            {
                Log.Name("PluginSignatureVerifier");
                Log.Info(I18nManager.S("security.fetching_public_key"));

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                var response = await client.GetStringAsync(SIGNATURE_URL);
                var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("content", out var contentElement))
                {
                    var newKey = contentElement.GetString();

                    if (!string.IsNullOrEmpty(newKey))
                    {
                        if (_publicKey != newKey)
                        {
                            _publicKey = newKey;
                            _configManager.UpdateConfig(cfg =>
                            {
                                cfg.PluginSignaturePublicKey = newKey;
                            });
                            Log.Info(I18nManager.S("security.public_key_updated"));
                        }
                        else
                        {
                            Log.Debug(I18nManager.S("security.public_key_unchanged"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(I18nManager.S("security.fetch_public_key_failed", ex.Message));
                if (!string.IsNullOrEmpty(_publicKey))
                {
                    Log.Info(I18nManager.S("security.using_cached_public_key"));
                }
            }
        }

        public bool VerifyDll(string dllPath)
        {
            if (_testMode)
            {
                Log.Debug(I18nManager.S("security.testmode_skip_verify", Path.GetFileName(dllPath)));
                return true;
            }

            if (string.IsNullOrEmpty(_publicKey))
            {
                Log.Warning(I18nManager.S("security.no_public_key"));
                return true;
            }

            if (!File.Exists(dllPath))
            {
                Log.Warning(I18nManager.S("security.dll_not_found", dllPath));
                return false;
            }

            try
            {
                var fileBytes = File.ReadAllBytes(dllPath);

                if (fileBytes.Length < 4)
                {
                    Log.Warning(I18nManager.S("security.no_signature_data", Path.GetFileName(dllPath)));
                    return false;
                }

                var sigLengthBytes = new byte[4];
                Array.Copy(fileBytes, fileBytes.Length - 4, sigLengthBytes, 0, 4);
                var sigLength = BitConverter.ToInt32(sigLengthBytes, 0);

                if (sigLength <= 0 || sigLength > fileBytes.Length - 4)
                {
                    Log.Warning(I18nManager.S("security.invalid_signature_data", Path.GetFileName(dllPath)));
                    return false;
                }

                var dllContentLength = fileBytes.Length - 4 - sigLength;
                var dllBytes = new byte[dllContentLength];
                Array.Copy(fileBytes, 0, dllBytes, 0, dllContentLength);

                var sigStringBytes = new byte[sigLength];
                Array.Copy(fileBytes, dllContentLength, sigStringBytes, 0, sigLength);
                var sigBase64 = Encoding.UTF8.GetString(sigStringBytes);
                var signatureBytes = Convert.FromBase64String(sigBase64);

                using var rsa = RSA.Create();
                rsa.ImportFromPem(_publicKey);

                bool verified = rsa.VerifyData(
                    dllBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );

                if (verified)
                {
                    Log.Debug(I18nManager.S("security.signature_verified", Path.GetFileName(dllPath)));
                }
                else
                {
                    Log.Warning(I18nManager.S("security.signature_failed", Path.GetFileName(dllPath)));
                }

                return verified;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("security.verify_error", Path.GetFileName(dllPath), ex.Message));
                return false;
            }
        }

        public bool VerifyDllByAssemblyPath(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
                return true;

            if (assemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                return true;

            return VerifyDll(assemblyPath);
        }
    }
}