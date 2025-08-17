using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

using NewLife;
using NewLife.Log;

using Pek.Configs;
using Pek.Permissions;

namespace Pek.OnlyOffice.Controllers;

/// <summary>
/// OnlyOffice代理控制器
/// </summary>
[ApiController]
[Route("_OnlyOffice")]
public class OnlyOfficeProxyController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="environment">Web主机环境</param>
    public OnlyOfficeProxyController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }
    /// <summary>
    /// 代理所有OnlyOffice请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <returns></returns>
    //[JwtAuthorize("OnlyOffice")]
    [HttpGet("{**path}")]
    [HttpPost("{**path}")]
    [HttpPut("{**path}")]
    [HttpDelete("{**path}")]
    [HttpPatch("{**path}")]
    public async Task<IActionResult> ProxyRequest(String path = "")
    {
        // 检查是否是WebSocket升级请求
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            // WebSocket请求由中间件处理，这里不应该到达
            XTrace.WriteLine("WebSocket请求应该由中间件处理，但到达了控制器");
            return BadRequest(new { error = "WebSocket请求处理异常" });
        }
        // 检查OnlyOfficeUrl是否为空
        if (OnlyOfficeSetting.Current.OnlyOfficeUrl.IsNullOrWhiteSpace())
        {
            XTrace.WriteLine($"OnlyOffice服务地址未配置，请在系统设置中配置OnlyOfficeUrl参数");
            return BadRequest(new { error = "OnlyOffice服务地址未配置，请在系统设置中配置OnlyOfficeUrl参数" });
        }

        var queryString = HttpContext.Request.QueryString;
        String targetUrlWithQueryString;
        String? xTargetUrl = null;

        // 根据运行环境决定使用代理还是直接请求
        if (_environment.IsDevelopment())
        {
            // 开发模式下使用代理服务器
            var proxyUrl = PekSysSetting.Current.LocalProxyUrl.TrimEnd('/');
            var targetPath = path.IsNullOrWhiteSpace() ? "" : $"/{path}";

            // 代理URL包含完整路径
            targetUrlWithQueryString = $"{proxyUrl}{targetPath}{queryString}";

            // X-Target-Url只包含目标域名，不包含路径
            xTargetUrl = OnlyOfficeSetting.Current.OnlyOfficeUrl.TrimEnd('/') + "/";

            XTrace.WriteLine($"开发模式: 代理请求 {targetUrlWithQueryString}, X-Target-Url: {xTargetUrl}");
        }
        else
        {
            // 生产模式下直接请求OnlyOffice服务
            var targetUrl = OnlyOfficeSetting.Current.OnlyOfficeUrl.TrimEnd('/');
            var targetPath = path.IsNullOrWhiteSpace() ? "" : $"/{path}";
            targetUrlWithQueryString = $"{targetUrl}{targetPath}{queryString}";

            XTrace.WriteLine($"生产模式: 直接请求 {targetUrlWithQueryString}");
        }

        using var client = new HttpClient();

        // 开发模式下添加代理头部
        if (_environment.IsDevelopment() && !xTargetUrl.IsNullOrWhiteSpace())
        {
            client.DefaultRequestHeaders.Add("X-Target-Url", xTargetUrl);
            client.DefaultRequestHeaders.Add("X-Token-Code", PekSysSetting.Current.LocalProxyCode);
        }

        var method = HttpContext.Request.Method;

        try
        {
            HttpResponseMessage response;

            switch (method.ToUpperInvariant())
            {
                case "GET":
                    response = await client.GetAsync(targetUrlWithQueryString).ConfigureAwait(false);
                    break;

                case "POST":
                    var postContent = await GetRequestContentAsync().ConfigureAwait(false);
                    response = await client.PostAsync(targetUrlWithQueryString, postContent).ConfigureAwait(false);
                    break;

                case "PUT":
                    var putContent = await GetRequestContentAsync().ConfigureAwait(false);
                    response = await client.PutAsync(targetUrlWithQueryString, putContent).ConfigureAwait(false);
                    break;

                case "DELETE":
                    response = await client.DeleteAsync(targetUrlWithQueryString).ConfigureAwait(false);
                    break;

                case "PATCH":
                    var patchContent = await GetRequestContentAsync().ConfigureAwait(false);
                    var patchRequest = new HttpRequestMessage(HttpMethod.Patch, targetUrlWithQueryString)
                    {
                        Content = patchContent
                    };
                    // 开发模式下为PATCH请求也添加代理头部
                    if (_environment.IsDevelopment() && !xTargetUrl.IsNullOrWhiteSpace())
                    {
                        patchRequest.Headers.Add("X-Target-Url", xTargetUrl);
                        patchRequest.Headers.Add("X-Token-Code", PekSysSetting.Current.LocalProxyCode);
                    }
                    response = await client.SendAsync(patchRequest).ConfigureAwait(false);
                    break;

                default:
                    return BadRequest(new { error = $"不支持的HTTP方法: {method}" });
            }

            // 复制响应头
            foreach (var header in response.Headers)
            {
                if (!HttpContext.Response.Headers.ContainsKey(header.Key))
                {
                    HttpContext.Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            // 复制内容头
            if (response.Content.Headers != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    if (!HttpContext.Response.Headers.ContainsKey(header.Key))
                    {
                        HttpContext.Response.Headers[header.Key] = header.Value.ToArray();
                    }
                }
            }

            // 设置状态码
            HttpContext.Response.StatusCode = (Int32)response.StatusCode;

            // 返回内容
            var content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new FileContentResult(content, response.Content.Headers?.ContentType?.ToString() ?? "application/octet-stream");
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = "OnlyOffice服务连接失败", details = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "代理请求处理失败", details = ex.Message });
        }
    }

    /// <summary>
    /// 获取请求内容
    /// </summary>
    /// <returns></returns>
    private async Task<HttpContent> GetRequestContentAsync()
    {
        if (HttpContext.Request.ContentLength == 0 || HttpContext.Request.ContentLength == null)
        {
            return new StringContent("", Encoding.UTF8);
        }

        using var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8);
        var requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
        
        var contentType = HttpContext.Request.ContentType ?? "application/json";
        return new StringContent(requestBody, Encoding.UTF8, contentType);
    }

    /// <summary>
    /// 获取WebSocket连接信息
    /// </summary>
    /// <returns></returns>
    [HttpGet("websocket/info")]
    public IActionResult GetWebSocketInfo()
    {
        var info = new
        {
            IsWebSocketRequest = HttpContext.WebSockets.IsWebSocketRequest,
            OnlyOfficeUrl = OnlyOfficeSetting.Current.OnlyOfficeUrl,
            Environment = _environment.EnvironmentName,
            ProxyUrl = _environment.IsDevelopment() ? PekSysSetting.Current.LocalProxyUrl : null,
            WebSocketSupported = true, // WebSocket is always supported
            Headers = HttpContext.Request.Headers
                .Where(h => h.Key.StartsWith("Sec-WebSocket") || h.Key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.ToString())
        };

        return Ok(info);
    }
}
