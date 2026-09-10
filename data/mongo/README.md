# MongoDB dump（提交到 Git）

本目录存放 `mongodump` 导出的业务库，供克隆仓库的人 `mongorestore` 后得到同一份初始/示例数据。

不要把 Docker 数据卷或 `WiredTiger` 目录提交进来。

## 库名

- `MetaDowAdministrationDb`
- `MetaDowIdentityServiceDb`
- `MetaDowProjectsDb`
- `MetaDowSaaSDb`

## 导出（AppHost 已启动且 Mongo 容器在跑）

在仓库根目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mongo-dump.ps1
```

然后 `git add data/mongo` 并提交。

## 还原

- **自动**：AppHost 在 Mongo 就绪后会执行 `tools/mongo-restore.ps1`。若本目录没有 `.bson`，则跳过。
- **手动**：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mongo-restore.ps1
```

空容器上还原后再跑 DbMigrator：已有 OpenIddict 客户端会被种子跳过；缺的种子仍会补上。

强制覆盖已有集合（慎用）：

```powershell
$env:MONGO_RESTORE_DROP = "1"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mongo-restore.ps1
```
