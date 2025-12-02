# 更新日志

本项目的所有重要更改都将记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
并且本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [未发布]

### 新增

- 实现 IDistributedCache 接口
- 二级缓存支持（本地内存缓存 + Redis）
- Redis 扩展功能（Hash、List、Set、Sorted Set、Pub/Sub）
- 强类型对象序列化支持
- 弹性和容错机制（使用 Polly）
- 性能指标收集和监控
- 健康检查集成
- 可配置的过期策略
- RabbitMQ 缓存失效通知支持
- 完整的单元测试覆盖

### 文档

- README.md
- CONTRIBUTING.md
- API 文档
- 示例项目

## [1.0.0] - YYYY-MM-DD

### 新增

- 初始版本发布
