using System;
using System.Security.Cryptography;
using System.Text;

namespace CoffeeBean
{
    /// <summary>
    /// 存档加密（对齐 Idle 项目的 EncryptHelp，补齐主存档加密缺口）：
    ///
    /// - **AES-256-CBC + PKCS7**：key 由项目配置（<see cref="CSaveOptions.EncryptionKey"/>）；
    ///   每次写盘生成随机 IV 并前置到密文头部（同明文不同密文，防模式识别）
    /// - **XOR 混淆外层**（可选）：进一步打散字节，防直接查看
    ///
    /// **安全边界**：混淆级保护（key 硬编码在客户端），防普通读取 / 防小白，
    /// 不防专业逆向；真安全需服务器校验存档。
    /// </summary>
    public static class CSaveEncrypt
    {
        private const int IvSize = 16;

        /// <summary>AES 加密：随机 IV + 密文（IV 前置）。</summary>
        public static byte[] Encrypt(byte[] data, string key)
        {
            byte[] keyBytes = DeriveKey(key);
            using Aes aes = Aes.Create();
            aes.Key = keyBytes;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] body = encryptor.TransformFinalBlock(data, 0, data.Length);

            var result = new byte[IvSize + body.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, IvSize);
            Buffer.BlockCopy(body, 0, result, IvSize, body.Length);
            return result;
        }

        /// <summary>AES 解密（密文 = IV + body）。</summary>
        public static byte[] Decrypt(byte[] cipher, string key)
        {
            if (cipher == null || cipher.Length <= IvSize)
                throw new InvalidOperationException("存档密文长度非法（缺少 IV）");

            byte[] keyBytes = DeriveKey(key);
            using Aes aes = Aes.Create();
            aes.Key = keyBytes;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            // 注意：IV setter 会克隆数组，必须先填好再整体赋值（先赋值空数组再 BlockCopy 填充无效）
            byte[] iv = new byte[IvSize];
            Buffer.BlockCopy(cipher, 0, iv, 0, IvSize);
            aes.IV = iv;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipher, IvSize, cipher.Length - IvSize);
        }

        /// <summary>XOR 混淆（可逆；与 AES 组合进一步提高门槛）。</summary>
        public static byte[] Xor(byte[] data, byte keyByte)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ keyByte ^ (byte)(i & 0x7F));
            return result;
        }

        /// <summary>key 字符串 → 32 字节 AES key（SHA-256）。</summary>
        private static byte[] DeriveKey(string key)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? string.Empty));
        }
    }
}
