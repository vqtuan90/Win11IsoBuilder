using Win11IsoBuilder.Services.Dism;

namespace Win11IsoBuilder.Tests;

public class DismOutputParserTests
{
    private const string ImageInfoSample = """
        Deployment Image Servicing and Management tool
        Version: 10.0.22621.1

        Details for image : D:\sources\install.wim

        Index : 1
        Name : Windows 11 Home
        Description : Windows 11 Home
        Size : 15,895,704,123 bytes

        Index : 2
        Name : Windows 11 Pro
        Description : Windows 11 Pro
        Size : 16,123,456,789 bytes

        The operation completed successfully.
        """;

    private const string AppxSample = """
        Deployment Image Servicing and Management tool

        Packages listing:

        DisplayName : Microsoft.BingNews
        PackageName : Microsoft.BingNews_4.55.62231.0_neutral_~_8wekyb3d8bbwe
        Architecture : neutral

        DisplayName : Microsoft.GamingApp
        PackageName : Microsoft.GamingApp_2021.427.138.0_neutral_~_8wekyb3d8bbwe

        The operation completed successfully.
        """;

    [Fact]
    public void ParseImageInfo_ReturnsAllEditions()
    {
        var editions = DismOutputParser.ParseImageInfo(ImageInfoSample);

        Assert.Equal(2, editions.Count);
        Assert.Equal(1, editions[0].Index);
        Assert.Equal("Windows 11 Home", editions[0].Name);
        Assert.Equal(15_895_704_123, editions[0].SizeBytes);
        Assert.Equal("Windows 11 Pro", editions[1].Name);
    }

    [Fact]
    public void ParseProvisionedAppx_PairsDisplayAndPackageNames()
    {
        var pkgs = DismOutputParser.ParseProvisionedAppx(AppxSample);

        Assert.Equal(2, pkgs.Count);
        Assert.Equal("Microsoft.BingNews", pkgs[0].DisplayName);
        Assert.StartsWith("Microsoft.BingNews_4.55", pkgs[0].PackageName);
        Assert.Equal("Microsoft.GamingApp", pkgs[1].DisplayName);
    }

    [Fact]
    public void ParseImageInfo_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(DismOutputParser.ParseImageInfo(string.Empty));
    }
}
