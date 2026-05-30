using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class OfficeOdtServiceTests
{
    private readonly OfficeOdtService _svc = new(new ProcessRunner(), new NoOpLog());

    [Fact]
    public void BuildConfigurationXml_UsesProductBitnessChannel()
    {
        var o = new OfficeOptions { ProductId = "O365BusinessRetail", Bitness = 64, Channel = "MonthlyEnterprise" };

        var add = _svc.BuildConfigurationXml(o, null).Root!.Element("Add")!;

        Assert.Equal("64", add.Attribute("OfficeClientEdition")!.Value);
        Assert.Equal("MonthlyEnterprise", add.Attribute("Channel")!.Value);
        Assert.Equal("O365BusinessRetail", add.Element("Product")!.Attribute("ID")!.Value);
    }

    [Fact]
    public void BuildConfigurationXml_IncludesLanguagesAndExclusions()
    {
        var o = new OfficeOptions
        {
            Languages = { },
            ExcludedApps = { "Access", "Publisher" },
        };
        o.Languages.Clear();
        o.Languages.Add("en-us");

        var product = _svc.BuildConfigurationXml(o, null).Root!.Element("Add")!.Element("Product")!;

        Assert.Contains(product.Elements("Language"), e => e.Attribute("ID")!.Value == "en-us");
        var excluded = product.Elements("ExcludeApp").Select(e => e.Attribute("ID")!.Value).ToList();
        Assert.Contains("Access", excluded);
        Assert.Contains("Publisher", excluded);
    }

    [Fact]
    public void BuildConfigurationXml_SourcePathOnlyWhenProvided()
    {
        var o = new OfficeOptions();

        var withPath = _svc.BuildConfigurationXml(o, @"C:\cache").Root!.Element("Add")!;
        var without = _svc.BuildConfigurationXml(o, null).Root!.Element("Add")!;

        Assert.Equal(@"C:\cache", withPath.Attribute("SourcePath")!.Value);
        Assert.Null(without.Attribute("SourcePath"));
    }

    [Fact]
    public void BuildConfigurationXml_AlwaysRemovesMsiAndHidesDisplay()
    {
        var root = _svc.BuildConfigurationXml(new OfficeOptions(), null).Root!;

        Assert.NotNull(root.Element("RemoveMSI"));
        Assert.Equal("None", root.Element("Display")!.Attribute("Level")!.Value);
    }
}
