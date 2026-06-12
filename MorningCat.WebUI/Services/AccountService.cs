using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MorningCat.WebUI.Services
{
    public class AccountService : IAccountService
    {
        private string _username;
        private string _passwordHash;
        private string _currentPassword;
        private bool _isPasswordChanged = false;
        private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
        private readonly TimeSpan _tokenExpiry = TimeSpan.FromHours(24);

        public event Action<string, string>? CredentialsChanged;

        public AccountService(string username = "admin", string password = "admin123")
        {
            _username = username;
            _currentPassword = password;
            _passwordHash = HashPassword(password);
        }

        public bool IsPasswordChanged => _isPasswordChanged;

        public string CurrentUsername => _username;

        public (string Username, string Password) GetDefaultCredentials()
        {
            return (_username, "******");
        }

        public string GetPassword()
        {
            return _currentPassword;
        }

        public bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            if (_tokens.TryGetValue(token, out var expiry))
            {
                if (DateTime.UtcNow - expiry < _tokenExpiry)
                {
                    return true;
                }
                _tokens.TryRemove(token, out _);
            }
            return false;
        }

        public string GenerateToken()
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _tokens[token] = DateTime.UtcNow;
            return token;
        }

        public bool ValidateCredentials(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            return username == _username && HashPassword(password) == _passwordHash;
        }

        public bool ChangePassword(string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
                return false;

            if (HashPassword(oldPassword) != _passwordHash)
                return false;

            if (newPassword.Length < 6)
                return false;

            _passwordHash = HashPassword(newPassword);
            _currentPassword = newPassword;
            _isPasswordChanged = true;
            
            foreach (var token in _tokens.Keys)
            {
                _tokens.TryRemove(token, out _);
            }

            CredentialsChanged?.Invoke(_username, newPassword);
            
            return true;
        }

        public bool ChangeUsername(string newUsername)
        {
            if (string.IsNullOrEmpty(newUsername) || newUsername.Length < 3)
                return false;

            _username = newUsername;
            _isPasswordChanged = true;

            foreach (var token in _tokens.Keys)
            {
                _tokens.TryRemove(token, out _);
            }

            CredentialsChanged?.Invoke(_username, _currentPassword);
            
            return true;
        }

        public void ForceChangePassword(string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
                return;
            
            _passwordHash = HashPassword(newPassword);
            _currentPassword = newPassword;
            _isPasswordChanged = true;

            CredentialsChanged?.Invoke(_username, newPassword);
        }

        public void UpdatePassword(string newPassword)
        {
            ForceChangePassword(newPassword);
        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "MorningCat_Salt"));
            return Convert.ToBase64String(bytes);
        }
    }
}
