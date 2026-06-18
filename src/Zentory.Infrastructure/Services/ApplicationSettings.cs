using Microsoft.Extensions.Configuration;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class ApplicationSettings : IApplicationSettings
{
    public string BaseUrl { get; }

    public ApplicationSettings(IConfiguration configuration)
    {
        BaseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://app.zentory.co";
    }
}
