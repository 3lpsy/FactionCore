/*
References used in this section:
https://gist.github.com/doncadavona/fd493b6ced456371da8879c22bb1c263
https://blog.bitscry.com/2018/04/13/cryptographically-secure-random-string/
http://blogs.interknowlogy.com/2012/06/08/providing-integrity-for-encrypted-data-with-hmacs-in-net/
 */

using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using Faction.Common.Models;

namespace Faction.Core.Handlers
{
  public static class Crypto
  {
    // Generate a new AES key for Agents
    public static Dictionary<string, byte[]> GenerateAgentKey_Aes()
    {
      Dictionary<string, byte[]> agentKeys = new Dictionary<string, byte[]>();
      try
      {
        using (Aes AesKey = Aes.Create())
        {
          agentKeys.Add("AesKey", AesKey.Key);
          agentKeys.Add("AesIV", AesKey.IV);
        }
      }
      catch (CryptographicException e)
      {
        Console.WriteLine(e.Message);
      }

      return agentKeys;
    }


    
    public static Dictionary<string, string> Encrypt(string plainMessageJson, int AgentId, string agentPassword)
    {
      try
      {
        RijndaelManaged aes = new RijndaelManaged();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Padding = PaddingMode.PKCS7;
        aes.Mode = CipherMode.CBC;

        aes.Key = Encoding.UTF8.GetBytes(agentPassword);
        aes.GenerateIV();

        ICryptoTransform AESEncrypt = aes.CreateEncryptor(aes.Key, aes.IV);
        byte[] buffer = Encoding.UTF8.GetBytes(plainMessageJson);

        string encryptedText = Convert.ToBase64String(AESEncrypt.TransformFinalBlock(buffer, 0, buffer.Length));

        string hmac = Convert.ToBase64String(HmacSHA256(Convert.ToBase64String(aes.IV) + encryptedText, agentPassword));

        return new Dictionary<string, string>
          {
            { "agent_id", AgentId.ToString() },
            { "iv", Convert.ToBase64String(aes.IV) },
            { "encryptedMsg", encryptedText },
            { "hmac", hmac },
          };
      }
      catch (Exception e)
      {
        throw new Exception("Error encrypting: " + e.Message);
      }
    }

    public static string Decrypt(StagingMessage stagingMessage)
    {
      byte[] password = Encoding.UTF8.GetBytes(stagingMessage.Payload.Key);
      byte[] iv = Convert.FromBase64String(stagingMessage.IV);      
      byte[] hmac = Convert.FromBase64String(stagingMessage.HMAC);
      string message = stagingMessage.Message;
      return Decrypt(iv, password, message, hmac);
    }

    public static string Decrypt(AgentCheckin taskResponse)
    {
      byte[] password = Encoding.UTF8.GetBytes(taskResponse.Agent.AesPassword);
      byte[] iv = Convert.FromBase64String(taskResponse.IV);
      byte[] hmac = Convert.FromBase64String(taskResponse.HMAC);
      string message = taskResponse.Message;
      return Decrypt(iv, password, message, hmac);
    }

    public static string Decrypt(byte[] iv, byte[] password, string message, byte[] hmac)
    {
      try
      {
        if (ValidateEncryptedData(iv, message, password, hmac)) {
          Console.WriteLine("HMAC PASSED!");
          RijndaelManaged aes = new RijndaelManaged();
          aes.KeySize = 256;
          aes.BlockSize = 128;
          aes.Padding = PaddingMode.PKCS7;
          aes.Mode = CipherMode.CBC;
          aes.Key = password;
          aes.IV = iv;

          ICryptoTransform AESDecrypt = aes.CreateDecryptor(aes.Key, aes.IV);
          byte[] buffer = Convert.FromBase64String(message);

          return Encoding.UTF8.GetString(AESDecrypt.TransformFinalBlock(buffer, 0, buffer.Length));
        }
        else {
          throw new Exception("HMAC verification failed.");
        }
      }
      catch (Exception e)
      {
        throw new Exception("Error decrypting: " + e.Message);
      }
    }

    static byte[] HmacSHA256(String data, String key)
    {
      using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
      {
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
      }
    }

    public static bool ValidateEncryptedData(byte[] iv, string encryptedText, byte[] agentPassword, byte[] recievedHmac)
    {
      string password = Encoding.UTF8.GetString(agentPassword);
      byte[] calculatedHmac = HmacSHA256(Convert.ToBase64String(iv) + encryptedText, password);
      return BytesAreEqual(calculatedHmac, recievedHmac);
    }

    // Checks to see if all the bytes in the two arrays are equal.
    // Returns fals if either of the arrays are null or not the same length.
    public static bool BytesAreEqual(byte[] array1, byte[] array2)
    {
        if (array1 == null || array2 == null || array1.Length != array2.Length)
            return false;
 
        if (array1.Length == 0) return true;
 
        for (int i = 0; i < array1.Length; i++)
        {
            if (array1[i] != array2[i])
                return false;
        }
 
        return true;
    }
  }
}