using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Tessera.Storage;

public sealed class S3ObjectStorage : IObjectStorage
{
    private readonly StorageOptions _options;
    private readonly TimeProvider _clock;
    private readonly IAmazonS3 _s3;

    public S3ObjectStorage(IOptions<StorageOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;

        var config = new AmazonS3Config { ForcePathStyle = _options.ForcePathStyle };
        if (!string.IsNullOrEmpty(_options.ServiceUrl))
        {
            config.ServiceURL = _options.ServiceUrl;
            config.AuthenticationRegion = _options.Region;
        }
        else
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region);
        }

        _s3 = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), config);
    }

    // Builds and signs an S3 POST policy by hand: the AWS SDK for .NET has no
    // presigned-POST helper. The content-length-range condition is what makes object
    // storage reject an oversized upload itself.
    public PresignedUpload CreatePresignedUpload(string key, string contentType, long maxBytes, TimeSpan expiry)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var credential = $"{_options.AccessKey}/{dateStamp}/{_options.Region}/s3/aws4_request";
        var expiration = now.Add(expiry).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var policy = new
        {
            expiration,
            conditions = new object[]
            {
                new Dictionary<string, string> { ["bucket"] = _options.Bucket },
                new Dictionary<string, string> { ["key"] = key },
                new Dictionary<string, string> { ["Content-Type"] = contentType },
                new object[] { "content-length-range", 1, maxBytes },
                new Dictionary<string, string> { ["x-amz-algorithm"] = "AWS4-HMAC-SHA256" },
                new Dictionary<string, string> { ["x-amz-credential"] = credential },
                new Dictionary<string, string> { ["x-amz-date"] = amzDate },
            },
        };

        var policyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(policy)));
        var signingKey = SigningKey(_options.SecretKey, dateStamp, _options.Region);
        var signature = Hex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(policyBase64)));

        var fields = new Dictionary<string, string>
        {
            ["key"] = key,
            ["Content-Type"] = contentType,
            ["x-amz-algorithm"] = "AWS4-HMAC-SHA256",
            ["x-amz-credential"] = credential,
            ["x-amz-date"] = amzDate,
            ["policy"] = policyBase64,
            ["x-amz-signature"] = signature,
        };

        return new PresignedUpload(UploadUrl(), fields);
    }

    public async Task<byte[]> ReadHeadAsync(string key, int byteCount, CancellationToken ct)
    {
        var request = new GetObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            ByteRange = new ByteRange(0, byteCount - 1),
        };

        using var response = await _s3.GetObjectAsync(request, ct);
        using var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    public async Task<long?> GetSizeAsync(string key, CancellationToken ct)
    {
        try
        {
            var metadata = await _s3.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct)
        => _s3.DeleteObjectAsync(_options.Bucket, key, ct);

    public async Task DownloadToFileAsync(string key, string filePath, CancellationToken ct)
    {
        using var response = await _s3.GetObjectAsync(_options.Bucket, key, ct);
        await response.WriteResponseStreamToFileAsync(filePath, append: false, ct);
    }

    public Task UploadFileAsync(string key, string filePath, string contentType, CancellationToken ct)
        => _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            FilePath = filePath,
            ContentType = contentType,
        }, ct);

    public async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_s3, _options.Bucket))
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
        }
    }

    private string UploadUrl()
        => string.IsNullOrEmpty(_options.ServiceUrl)
            ? $"https://{_options.Bucket}.s3.{_options.Region}.amazonaws.com"
            : $"{_options.ServiceUrl.TrimEnd('/')}/{_options.Bucket}";

    private static byte[] SigningKey(string secret, string dateStamp, string region)
    {
        var kDate = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secret), Encoding.UTF8.GetBytes(dateStamp));
        var kRegion = HMACSHA256.HashData(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HMACSHA256.HashData(kRegion, Encoding.UTF8.GetBytes("s3"));
        return HMACSHA256.HashData(kService, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
