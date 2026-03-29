using Amazon.S3;
using Amazon.S3.Model;

namespace Northwoods.Tenancy;

/// <summary>
/// S3-compatible object storage wrapper for MinIO integration.
/// </summary>
public sealed class ObjectStore
{
    private readonly AmazonS3Client _s3Client;
    private readonly string _bucketName;

    public ObjectStore(string endpoint, string accessKey, string secretKey, string bucketName)
    {
        _bucketName = bucketName;

        _s3Client = new AmazonS3Client(accessKey, secretKey, CreateConfig(endpoint));
    }

    private static AmazonS3Config CreateConfig(string endpoint) => new()
    {
        ServiceURL = endpoint.Contains("://") ? endpoint : $"http://{endpoint}",
        ForcePathStyle = true,
        UseHttp = !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
        AuthenticationRegion = "us-east-1",
    };

    public async Task UploadAsync(string key, Stream data, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = data,
            ContentType = contentType,
        };

        await _s3Client.PutObjectAsync(request);
    }

    public async Task<byte[]> DownloadAsync(string key)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, key);
        await using var stream = response.ResponseStream;
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public async Task<GetObjectResponse> GetObjectStreamAsync(string key)
    {
        return await _s3Client.GetObjectAsync(_bucketName, key);
    }

    public async Task EnsureBucketAsync()
    {
        try
        {
            var response = await _s3Client.ListBucketsAsync();
            var bucketExists = response.Buckets?.Any(b => b.BucketName == _bucketName) ?? false;

            if (!bucketExists)
            {
                var putRequest = new PutBucketRequest { BucketName = _bucketName };
                await _s3Client.PutBucketAsync(putRequest);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to ensure bucket '{_bucketName}' exists", ex);
        }
    }
}
