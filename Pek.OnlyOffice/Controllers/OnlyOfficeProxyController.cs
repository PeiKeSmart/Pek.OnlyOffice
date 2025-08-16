using System.Text;

using Microsoft.AspNetCore.Mvc;

using NewLife;
using NewLife.Log;

using Pek.Permissions;

namespace Pek.OnlyOffice.Controllers;

/// <summary>
/// OnlyOffice代理控制器
/// </summary>
[ApiController]
[Route("_OnlyOffice")]
public class OnlyOfficeProxyController : ControllerBase
{
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
        // 检查OnlyOfficeUrl是否为空
        if (OnlyOfficeSetting.Current.OnlyOfficeUrl.IsNullOrWhiteSpace())
        {
            XTrace.WriteLine($"OnlyOffice服务地址未配置，请在系统设置中配置OnlyOfficeUrl参数");
            return BadRequest(new { error = "OnlyOffice服务地址未配置，请在系统设置中配置OnlyOfficeUrl参数" });
        }

        var queryString = HttpContext.Request.QueryString;
        var targetUrl = OnlyOfficeSetting.Current.OnlyOfficeUrl.TrimEnd('/');
        var targetUrlWithQueryString = $"{targetUrl}/{path}{queryString}";

        using var client = new HttpClient();
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
}
