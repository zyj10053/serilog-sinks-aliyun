using Newtonsoft.Json;
using Serilog.Events;
using Serilog.Formatting.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Serilog.Sinks.Aliyun.Helper
{
    public static class StringHelper
    {
        private static string desKey = "20150425@)!^)*#!";

        /// <summary>
        /// 构造加密Key
        /// </summary>
        /// <param name="desKey">DES加密Key</param>
        /// <returns>加密Key</returns>
        private static string GetKey(string desKey)
        {
            string text = string.Empty;
            Regex regex = new Regex("\\d");
            int num = regex.Match(desKey).Index;
            string s = desKey.PadLeft(1, '0').Substring(num, 1);
            if (num > desKey.Length - 2)
            {
                num = desKey.Length - 4;
            }

            text += desKey.PadLeft(2, '0').Substring(num + 1, 2);
            int.TryParse(s, out num);
            if (num > desKey.Length - 2)
            {
                num = desKey.Length - 5;
            }

            text += desKey.PadLeft(2, '0').Substring(num, 2);
            if (num < 2)
            {
                num = 2;
            }

            text += desKey.PadLeft(2, '0').Substring(desKey.Length - num, 2);
            text = text + desKey.PadLeft(1, '0').Substring(0, 1) + desKey.PadLeft(1, '0').Substring(desKey.Length - 1, 1);
            return text.PadLeft(8, '0').Substring(0, 8);
        }

        /// <summary>
        /// 构造加密向量
        /// </summary>
        /// <param name="desKey">DES加密Key</param>
        /// <returns>加密向量</returns>
        private static string GetIV(string desKey)
        {
            string text = string.Empty;
            Regex regex = new Regex("\\d");
            int num = regex.Match(desKey).Index;
            string s = desKey.PadLeft(1, '0').Substring(num, 1);
            if (num > desKey.Length - 2)
            {
                num = desKey.Length - 6;
            }

            text += desKey.PadLeft(2, '0').Substring(num + 2, 2);
            int.TryParse(s, out num);
            if (num > desKey.Length - 2)
            {
                num = desKey.Length - 9;
            }

            text += desKey.PadLeft(2, '0').Substring(num + 2, 2);
            if (num < 2)
            {
                num = 4;
            }

            text += desKey.PadLeft(2, '0').Substring(desKey.Length - num - 2, 2);
            text = text + desKey.PadLeft(1, '0').Substring(1, 1) + desKey.PadLeft(1, '0').Substring(desKey.Length - 2, 1);
            text = Reverse(text);
            return text.PadLeft(8, '0').Substring(0, 8);
        }

        /// <summary>
        /// 倒置字符串
        /// </summary>
        /// <param name="reverseString">待倒置的字符串</param>
        /// <returns>倒置后的字符串</returns>
        private static string Reverse(string reverseString)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = reverseString.Length; i > 0; i--)
            {
                builder.Append(reverseString.Substring(i - 1, 1));
            }

            return builder.ToString();
        }

        /// <summary>
        /// DES解密
        /// </summary>
        /// <param name="decryptoContext">待解密字符串</param>
        /// <param name="cryptoKey">加密Key</param>
        /// <returns>解密后字符串</returns>
        public static string DESDecrypt(string decryptoContext)
        {
            try
            {
                string key = GetKey(desKey);
                string iv = GetIV(desKey);
                byte[] keyByte = Encoding.UTF8.GetBytes(key);
                byte[] ivByte = Encoding.UTF8.GetBytes(iv);
                byte[] contextByte = System.Convert.FromBase64String(decryptoContext);
                DESCryptoServiceProvider dESCryptoServiceProvider = new DESCryptoServiceProvider();
                dESCryptoServiceProvider.Mode = CipherMode.ECB;
                MemoryStream memoryStream = new MemoryStream();
                CryptoStream cryptoStream = new CryptoStream(memoryStream, dESCryptoServiceProvider.CreateDecryptor(keyByte, ivByte), CryptoStreamMode.Write);
                cryptoStream.Write(contextByte, 0, contextByte.Length);
                cryptoStream.FlushFinalBlock();
                string result = Encoding.UTF8.GetString(memoryStream.ToArray());
                cryptoStream.Close();
                memoryStream.Close();
                return result;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 移除空行
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string RemoveNewline(string str)
        {
            str ??= "";
            str = JsonConvert.SerializeObject(str);
            str = Regex.Replace(str, @"\\r|\\n|\\s", "");
            return str;
        }

        public static string FormatToJson(LogEvent @event)
        {
            var formatter = new JsonFormatter(renderMessage: true);
            var output = new StringWriter();
            formatter.Format(@event, output);
            return output.ToString();
        }

        public static string ConvertTo(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            Type type = value.GetType();
            if (type.IsClass)
            {
                return JsonConvert.SerializeObject(value);
            }

            return value.ToString();
        }
    }
}
