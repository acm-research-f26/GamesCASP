using System;
using System.IO;
using UnityEngine;

public static class EnvLoader
{
    public static void Load()
    {
        string path = Path.Combine(
            Directory.GetCurrentDirectory(), "../.env"
        );

        if (!File.Exists(path))
        {
            Debug.LogWarning(".env file not found!");
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            string[] parts = trimmed.Split('=', 2);

            if (parts.Length != 2)
                continue;

            Environment.SetEnvironmentVariable(
                parts[0].Trim(),
                parts[1].Trim()
            );
        }
    }

    public static string Get(string key)
    {
        return Environment.GetEnvironmentVariable(key);
    }
}