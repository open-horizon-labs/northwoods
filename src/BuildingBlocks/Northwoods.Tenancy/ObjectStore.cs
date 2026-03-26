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

        var s3Config = new AmazonS3Config
        {
            ServiceURL = $"http://{endpoint}",
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1"
        };

        _s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
    }

    /// <summary>
    /// Uploads a file to the object store.
    /// </summary>
    /// <param name="key">The object key/path</param>
    /// <param name="data">The file data stream</param>
    /// <param name="contentType">The MIME type of the content</param>
    public async Task UploadAsync(string key, Stream data, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = data,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request);
    }

    /// <summary>
    /// Generates a presigned URL for retrieving an object.
    /// </summary>
    /// <param name="key">The object key/path</param>
    /// <param name="expiry">The duration for which the URL is valid</param>
    /// <returns>A presigned URL for GET requests</returns>
    public string GetPresignedUrl(string key, TimeSpan expiry)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET
        };

        return _s3Client.GetPreSignedURL(request);
    }

    /// <summary>
    /// Ensures the bucket exists, creating it if necessary.
    /// </summary>
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
