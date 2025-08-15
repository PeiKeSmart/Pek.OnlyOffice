using System.ComponentModel;

using NewLife.Configuration;

namespace Pek.OnlyOffice;

/// <summary>OnlyOffice设置</summary>
[DisplayName("OnlyOffice设置")]
[Config("AMapSetting")]
public class OnlyOfficeSetting : Config<OnlyOfficeSetting>
{
    /// <summary>
    /// OnlyOffice地址
    /// </summary>
    [Description("OnlyOffice地址")]
    public String? OnlyOfficeUrl { get; set; }
}
