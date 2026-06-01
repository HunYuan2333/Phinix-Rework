# 框架 Protobuf 协议 Schema 定义

> 三流骨架 proto 已落地 `Common/Utils/Framework/Proto/`。本文档描述当前目标 Schema 及实现状态。

---

## 1. 共享 Header — `FrameworkHeader`

```
framework_version + flow + type_id + packet_id + session_id + sender_uuid + timestamp + metadata
```

已落地：`Proto/Shared/FrameworkShared.proto`（含 `FrameworkMetadataEntry`、`FrameworkFlow` 枚举、`FrameworkHeader`）

---

## 2. Message 流

```proto
message FrameworkMessagePacket {
  FrameworkHeader header = 1;
  bytes payload_bytes = 2;
}
```

已落地：`Proto/Message/FrameworkMessagePacket.proto`

---

## 3. Command 流

```proto
enum FrameworkCommandKind {
  UNSPECIFIED=0; REQUEST=1; RESPONSE=2; STATE=3; EVENT=4;
}
message FrameworkCommandPacket {
  FrameworkHeader header = 1;
  FrameworkCommandKind command_kind = 2;
  bytes payload_bytes = 3;
}
```

已落地：`Proto/Command/FrameworkCommandPacket.proto`

---

## 4. Item 流

```proto
message FrameworkItemPacket {
  FrameworkHeader header = 1;
  string codec_id = 2;
  bytes payload_bytes = 3;
}
message FrameworkVanillaItemData { ... }
message FrameworkItemCollection { repeated FrameworkItemPacket items = 1; }
enum FrameworkItemQuality { AWFUL=1 ... LEGENDARY=7; NONE=8; }
```

已落地：`Proto/Item/FrameworkItemPacket.proto`

实际代码中已使用 `FrameworkVanillaItemData` + `FrameworkItemQuality`（通过 `DefaultLegacyTradeItemCodec`，codecId=`core.item.vanilla`）。

---

## 5. 迁移指引

- 新框架可见代码直接面向 message/command/item 编写
- 旧模块通过 adapter 适配到新三流
- 迁移收敛到永久流管线，避免临时第二套旧边界

---

## 6. 相关文档

- [框架Protobuf协议设计.md](框架Protobuf协议设计.md)
- [设计哲学.md](设计哲学.md)
- [Phase6-Core级宿主与动态扩展架构.md](Phase6-Core级宿主与动态扩展架构.md)
