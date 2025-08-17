# OnlyOffice WebSocket 代理功能

## 概述

本项目现已支持WebSocket请求的代理转发，可以在开发模式和生产模式下正常工作，支持OnlyOffice的实时协作功能。

## 功能特性

- ✅ **双模式支持**: 开发模式使用代理服务器，生产模式直接连接
- ✅ **实时双向通信**: 支持客户端与OnlyOffice服务器之间的WebSocket消息转发
- ✅ **自动连接管理**: 自动处理连接建立、消息转发和连接关闭
- ✅ **错误处理**: 完善的异常处理和日志记录
- ✅ **调试支持**: 提供连接信息查询接口和测试页面

## 架构说明

### 开发模式
```
客户端 <-> OnlyOfficeWebSocketProxyMiddleware <-> 代理服务器 <-> OnlyOffice服务器
```

### 生产模式
```
客户端 <-> OnlyOfficeWebSocketProxyMiddleware <-> OnlyOffice服务器
```

## 配置要求

### 1. 必需配置
- `OnlyOfficeSetting.OnlyOfficeUrl`: OnlyOffice服务器地址
- 开发模式还需要:
  - `PekSysSetting.LocalProxyUrl`: 本地代理服务器地址
  - `PekSysSetting.LocalProxyCode`: 代理服务器认证码

### 2. 项目依赖
已自动添加以下NuGet包:
- `System.Net.WebSockets.Client`: WebSocket客户端支持

## 使用方法

### WebSocket连接地址
所有OnlyOffice的WebSocket请求都应该使用以下格式:
```
ws://your-domain/_OnlyOffice/your-websocket-path
```

例如:
```javascript
const ws = new WebSocket('ws://localhost:5000/_OnlyOffice/websocket');
```

### 测试页面
访问 `/wwwroot/websocket-test.html` 可以进行WebSocket连接测试。

### 连接信息查询
GET请求 `/_OnlyOffice/websocket/info` 可以获取当前WebSocket配置信息。

## 实现细节

### 1. 中间件处理流程
1. 检查请求路径是否以 `/_OnlyOffice` 开头
2. 检查是否为WebSocket升级请求
3. 根据环境模式构建目标WebSocket URL
4. 建立到目标服务器的WebSocket连接
5. 启动双向消息转发

### 2. 消息转发机制
- 使用独立的Task处理双向消息转发
- 支持文本和二进制消息类型
- 自动处理连接关闭和异常情况
- 详细的日志记录便于调试

### 3. 错误处理
- WebSocket连接失败返回502状态码
- 配置错误返回400状态码
- 其他异常返回500状态码
- 所有错误都有详细的日志记录

## 日志输出

系统会输出以下类型的日志:
- WebSocket连接建立成功/失败
- 消息转发详情(方向、字节数、消息类型)
- 连接关闭和异常信息

## 故障排除

### 1. 连接失败
- 检查OnlyOfficeUrl配置是否正确
- 确认目标服务器支持WebSocket
- 查看日志中的详细错误信息

### 2. 消息转发异常
- 检查网络连接稳定性
- 确认代理服务器(开发模式)配置正确
- 查看XTrace日志输出

### 3. 开发模式问题
- 确认LocalProxyUrl和LocalProxyCode配置
- 检查代理服务器是否支持WebSocket转发
- 验证X-Target-Url和X-Token-Code头部设置

## 性能考虑

- WebSocket连接是长连接，注意连接数限制
- 消息转发使用4KB缓冲区，适合大多数场景
- 连接保活时间设置为2分钟
- 自动清理异常连接避免资源泄漏

## 安全注意事项

- 生产环境建议使用WSS(WebSocket over TLS)
- 代理模式下的认证码应该保密
- 建议对WebSocket连接进行适当的访问控制

## 版本兼容性

- 支持 .NET 8.0 和 .NET 9.0
- 兼容现有的HTTP代理功能
- 向后兼容，不影响现有功能
