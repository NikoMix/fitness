using System.Security.Cryptography;
using System.Text;

namespace Forge.Infrastructure.Media;

public static class MediaAssetKeys
{
    public static string ForExercise(string exerciseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(exerciseName.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }
}
