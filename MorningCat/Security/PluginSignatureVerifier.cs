using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Logging;
using MorningCat.Config;

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
                Log.Info("正在拉取插件签名公钥...");

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
                            Log.Info("插件签名公钥已更新并保存");
                        }
                        else
                        {
                            Log.Debug("插件签名公钥未变化");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"拉取插件签名公钥失败: {ex.Message}");
                if (!string.IsNullOrEmpty(_publicKey))
                {
                    Log.Info("将使用本地缓存的公钥");
                }
            }
        }

        public bool VerifyDll(string dllPath)
        {
            if (_testMode)
            {
                Log.Debug($"[TestMode] 跳过签名验证: {Path.GetFileName(dllPath)}");
                return true;
            }

            if (string.IsNullOrEmpty(_publicKey))
            {
                Log.Warning("未配置插件签名公钥，跳过签名验证");
                return true;
            }

            if (!File.Exists(dllPath))
            {
                Log.Warning($"DLL文件不存在: {dllPath}");
                return false;
            }

            try
            {
                var fileBytes = File.ReadAllBytes(dllPath);

                if (fileBytes.Length < 4)
                {
                    Log.Warning($"插件 {Path.GetFileName(dllPath)} 未包含签名数据");
                    return false;
                }

                var sigLengthBytes = new byte[4];
                Array.Copy(fileBytes, fileBytes.Length - 4, sigLengthBytes, 0, 4);
                var sigLength = BitConverter.ToInt32(sigLengthBytes, 0);

                if (sigLength <= 0 || sigLength > fileBytes.Length - 4)
                {
                    Log.Warning($"插件 {Path.GetFileName(dllPath)} 签名数据无效");
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
                    Log.Debug($"插件签名验证通过: {Path.GetFileName(dllPath)}");
                }
                else
                {
                    Log.Warning($"插件签名验证失败: {Path.GetFileName(dllPath)}");
                }

                return verified;
            }
            catch (Exception ex)
            {
                Log.Error($"验证插件签名时出错: {Path.GetFileName(dllPath)} - {ex.Message}");
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