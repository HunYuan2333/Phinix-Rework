# 客户端 Command Relay 示例

先构建客户端，再构建本项目。PhinixPackage 指向包含 Common/Assemblies 的实际客户端包目录。

```sh
dotnet build Samples/CommandRelay/CommandRelay.Sample.csproj -c Release -p:PhinixPackage=/absolute/path/to/phinix-rework
```

将生成的 CommandRelay.Sample.dll 放入客户端扩展发现目录。服务端使用支持 Relay 的 Framework，业务实现随客户端插件加载。

插件公开 IResearchRelaySample，其他客户端插件通过 API registry 获取它。RequestPlayer 发送玩家单播，Announce 发送按能力广播。两个 CommandType 分别声明接收能力，声明与 Handler 注册独立。停止模块时释放声明并使排队回调失效。

Received 在游戏主线程调用，参数为服务端确定的发送者 UUID 和原始 JSON。示例保留业务 Payload 的解释权，接入方自行解析数据、实现研究业务及业务 ACK。Send 方法返回路由结果，Submitted 表示网络提交。完整错误与同步状态契约见 ../../docs/Command-Capability-Relay.md。
