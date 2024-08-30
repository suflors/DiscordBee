namespace MusicBeePlugin.S3Client
{
  using Amazon.S3;
  using Amazon.S3.Model;
  using MusicBeePlugin.S3Client.Types;
  using System;
  using System.Drawing;
  using System.Drawing.Imaging;
  using System.Linq;
  using System.Threading.Tasks;

  public class S3Client : IDisposable
  {
    private readonly S3Config _config;
    private readonly AmazonS3Client _client;
    private readonly string _baseUrl;

    public S3Client(S3Config config)
    {
      _config = config;
      var clientConfig = new AmazonS3Config
      {
        ServiceURL = config.Endpoint,
      };
      _client = new AmazonS3Client(config.AccessKeyId, config.SecretAccessKey, clientConfig);
      _baseUrl = GetBaseUrl();
    }

    public async Task<S3Image[]> GetAlbumImages()
    {
      var listRequest = new ListObjectsV2Request
      {
        BucketName = _config.BucketName,
        Prefix = _config.Prefix,
      };

      var response = await _client.ListObjectsV2Async(listRequest);
      return response.S3Objects.Select(obj => new S3Image
      {
        Key = obj.Key,
        Link = $"{_baseUrl}{obj.Key}",
      }).ToArray();
    }

    public async Task<S3Image> UploadImage(string hash, string dataB64)
    {
      var key = $"{_config.Prefix}/{hash}";
      var imageStream = new System.IO.MemoryStream(Convert.FromBase64String(dataB64));

      // Get content type of image for S3
      string contentType = "image/unknown";
      using (var image = Image.FromStream(imageStream))
      {
        Guid guid = image.RawFormat.Guid;
        foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageDecoders())
        {
          if (codec.FormatID == guid)
          {
            contentType = codec.MimeType;
            break;
          }
        }
      }

      var putRequest = new PutObjectRequest
      {
        BucketName = _config.BucketName,
        Key = $"{_config.Prefix}/{hash}",
        ContentType = contentType,
        InputStream = imageStream,
        CannedACL = S3CannedACL.PublicRead,
      };

      var response = await _client.PutObjectAsync(putRequest);
      if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
      {
        return new S3Image
        {
          Key = key,
          Link = $"{_baseUrl}{key}",
        };
      } else
      {
        return null;
      }
    }

    public async Task<bool> TestConnection()
    {
      // Check if an S3 request can be successfully made
      try
      {
        var listRequest = new ListObjectsV2Request
        {
          BucketName = _config.BucketName,
          Prefix = _config.Prefix,
        };

        var response = await _client.ListObjectsV2Async(listRequest);
        return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
      }
      catch
      {
        return false;
      }
    }

    public void Dispose()
    {
      _client?.Dispose();
      GC.SuppressFinalize(this);
    }

    private string GetBaseUrl()
    {
      if (!string.IsNullOrEmpty(_config.CustomDomain))
      {
        return _config.CustomDomain;
      }

      // Fetch the bucket region
      var regionResponse = _client.GetBucketLocationAsync(new GetBucketLocationRequest
      {
        BucketName = _config.BucketName
      }).GetAwaiter().GetResult();

      var region = regionResponse.Location?.Value ?? "us-east-1"; // Default to us-east-1 if region is null

      // Prefix the bucket name to the endpoint's host
      var endpoint = new UriBuilder(_config.Endpoint);
      endpoint.Host = $"{_config.BucketName}.{endpoint.Host}";
      return endpoint.Uri.ToString();
    }
  }
}
