using System.Net.WebSockets;
using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

using NewLife;
using NewLife.Log;

using Pek.Configs;

namespace Pek.OnlyOffice.Middleware;

/// <summary>
/// OnlyOffice WebSocket代理中间件
/// </summary>
public class OnlyOfficeWebSocketProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="next">下一个中间件</param>
    /// <param name="environment">Web主机环境</param>
    public OnlyOfficeWebSocketProxyMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    /// <param name="context">HTTP上下文</param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // 检查是否是OnlyOffice路径的WebSocket请求
        if (context.Request.Path.StartsWithSegments("/_OnlyOffice") && context.WebSockets.IsWebSocketRequest)
        {
            await HandleWebSocketRequest(context).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理WebSocket请求
    /// </summary>
    /// <param name="context">HTTP上下文</param>
    /// <returns></returns>
    private async Task HandleWebSocketRequest(HttpContext context)
    {
        // 检查OnlyOfficeUrl是否为空
        if (OnlyOfficeSetting.Current.OnlyOfficeUrl.IsNullOrWhiteSpace())
        {
            XTrace.WriteLine("OnlyOffice服务地址未配置，无法建立WebSocket连接");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("OnlyOffice服务地址未配置").ConfigureAwait(false);
            return;
        }

        var path = context.Request.Path.Value?.Substring("/_OnlyOffice".Length) ?? "";
        var queryString = context.Request.QueryString;
        
        String targetWsUrl;
        
        // 根据运行环境决定使用代理还是直接连接
        if (_environment.IsDevelopment())
        {
            // 开发模式下使用代理服务器
            var proxyUrl = PekSysSetting.Current.LocalProxyUrl.TrimEnd('/');
            var wsProxyUrl = proxyUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            targetWsUrl = $"{wsProxyUrl}{path}{queryString}";
            
            XTrace.WriteLine($"开发模式: WebSocket代理连接 {targetWsUrl}");
        }
        else
        {
            // 生产模式下直接连接OnlyOffice服务
            var targetUrl = OnlyOfficeSetting.Current.OnlyOfficeUrl.TrimEnd('/');
            var wsTargetUrl = targetUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            targetWsUrl = $"{wsTargetUrl}{path}{queryString}";
            
            XTrace.WriteLine($"生产模式: WebSocket直接连接 {targetWsUrl}");
        }

        try
        {
            // 接受客户端WebSocket连接
            var clientWebSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            
            // 创建到目标服务器的WebSocket连接
            using var targetWebSocket = new ClientWebSocket();
            
            // 开发模式下添加代理头部
            if (_environment.IsDevelopment())
            {
                var targetBaseUrl = OnlyOfficeSetting.Current.OnlyOfficeUrl.TrimEnd('/') + "/";
                targetWebSocket.Options.SetRequestHeader("X-Target-Url", targetBaseUrl);
                targetWebSocket.Options.SetRequestHeader("X-Token-Code", PekSysSetting.Current.LocalProxyCode);
            }
            
            // 连接到目标WebSocket服务器
            await targetWebSocket.ConnectAsync(new Uri(targetWsUrl), CancellationToken.None).ConfigureAwait(false);
            
            XTrace.WriteLine($"WebSocket连接建立成功: {targetWsUrl}");
            
            // 开始双向转发
            var clientToTarget = ForwardWebSocketMessages(clientWebSocket, targetWebSocket, "客户端->目标");
            var targetToClient = ForwardWebSocketMessages(targetWebSocket, clientWebSocket, "目标->客户端");
            
            // 等待任一方向的连接关闭
            await Task.WhenAny(clientToTarget, targetToClient).ConfigureAwait(false);
            
            XTrace.WriteLine("WebSocket连接已关闭");
        }
        catch (WebSocketException ex)
        {
            XTrace.WriteLine($"WebSocket连接失败: {ex.Message}");
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync($"WebSocket连接失败: {ex.Message}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            XTrace.WriteLine($"WebSocket代理处理失败: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"WebSocket代理处理失败: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 转发WebSocket消息
    /// </summary>
    /// <param name="source">源WebSocket</param>
    /// <param name="target">目标WebSocket</param>
    /// <param name="direction">转发方向描述</param>
    /// <returns></returns>
    private static async Task ForwardWebSocketMessages(WebSocket source, WebSocket target, String direction)
    {
        var buffer = new Byte[4096];
        
        try
        {
            while (source.State == WebSocketState.Open && target.State == WebSocketState.Open)
            {
                var result = await source.ReceiveAsync(new ArraySegment<Byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await target.CloseAsync(WebSocketCloseStatus.NormalClosure, "连接关闭", CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                
                await target.SendAsync(
                    new ArraySegment<Byte>(buffer, 0, result.Count),
                    result.MessageType,
                    result.EndOfMessage,
                    CancellationToken.None).ConfigureAwait(false);
                
                XTrace.WriteLine($"WebSocket消息转发 ({direction}): {result.Count} 字节, 类型: {result.MessageType}");
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            XTrace.WriteLine($"WebSocket连接意外关闭 ({direction}): {ex.Message}");
        }
        catch (Exception ex)
        {
            XTrace.WriteLine($"WebSocket消息转发异常 ({direction}): {ex.Message}");
        }
    }
}
