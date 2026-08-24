# mep-device-connectors — 机电设备连接器合规检查

本技能用于检查有"设备编号"参数的设备(建筑运维认定)是否有 **connector 系统**、有 connector、且 connector 是否**已连接**(未连接=运维问题)。

## 何时使用

当用户需求涉及以下任一情况时,优先加载本技能再生成代码:

- "设备有没有 connector"
- "设备的 connector 都接上了吗"
- "设备有没有未连接的 connector"
- "设备连接器合规检查"

## 业务约定

- **设备认定**:有"设备编号"参数的 `FamilyInstance`(与 `mep-device-numbers` 同口径)。
- **ConnectorManager**:MEP 类别的设备才有。访问路径:
  - `inst.MEPModel`(MEP 类别的 FamilyInstance 才有;非 MEP 类别返回 null 或抛异常,需 try/catch)
  - `mepModel.ConnectorManager`
  - `cm.Connectors`(ConnectorSet,可 foreach 遍历)
- **Connector 关键成员**:`IsConnected`(是否已连接)。
- **已连接判定**:`conn.IsConnected == true` 即视为已连接。对端是否管道、对端 `Owner` 等更深拓扑在 2019 RevitAPI 不直接暴露(`Connector` 无 `GetConnectedConnector` 方法),故本技能只判"是否已连接",不判"对端是否管道"。
- **无 connector 系统**:非 MEP 类别的设备(如照明设备)可能没有 MEPModel,视为"无 connector 系统"。这类设备是否合规视业务而定——本技能默认标记为"无 connector 系统",在 issues 里提示,但不一定算不合格(取决于设备类别)。

## 不合格判定

- 无 connector 系统(非 MEP 设备) → 标记但不一定不合格(取决于设备类别,运维中可接受无管道连接的设备)
- 有 connector 系统,但无 connector → 不合格
- 有 connector 但有未连接的 → 不合格(运维要求 connector 已连接)

## 输出要求

返回匿名对象,字段:

- `DeviceCount`:设备总数
- `CompliantCount`:合格数(有 connector 系统且所有 connector 已连接)
- `NonCompliantCount`:不合格数
- `NoConnectorSystemCount`:无 connector 系统的设备数
- `DevicesWithUnconnectedCount`:有未连接 connector 的设备数
- `TotalConnectors`:所有 connector 总数
- `TotalConnected`:已连接总数
- `TotalUnconnected`:未连接总数
- `Issues`:不合格设备明细(前 50 个,字段:Id/Category/FamilyName/DeviceNumber/HasConnectorSystem/TotalConnectors/ConnectedCount/UnconnectedCount/Issue 原因)

## 模板

`templates/DeviceConnectorsCheck.cs` 实现 `IRevitDynamicCommand`,完成上述检查。用 try/catch 包裹 MEPModel 访问(兼容非 MEP 类别),用 var 推断 MEPModel/ConnectorManager 类型避免 namespace 问题。可直接使用或按需扩展(如按类别分别判定合格标准)。
