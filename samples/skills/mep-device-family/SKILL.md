# mep-device-family — 机电设备族类型合规检查

本技能用于检查有"设备编号"参数的设备(建筑运维认定)是否用**族**建的,不能用**体量(Mass)**建。

## 何时使用

当用户需求涉及以下任一情况时,优先加载本技能再生成代码:

- "设备是不是都用族建的"
- "有没有设备是用体量建的"
- "设备族类型合规检查"
- "设备建模方式检查"

## 业务约定

- **设备认定**:有"设备编号"参数的 `FamilyInstance`(与 `mep-device-numbers` 同口径)。
- **族实例**:`FamilyInstance` 类。建筑运维要求设备用族建,便于在项目里替换、统计、维护。
- **体量判断**:检查族类别(`inst.Symbol.Family.FamilyCategory`)是否为体量类别:
  - 判定方式:`familyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_Mass`。2019 RevitAPI 的 `Category` 无 `BuiltInCategory` 属性,改用族类别 ElementId 与枚举值比较(全版本通用),相等 → 概念体量族,不合规。
  - 体量族(MassFamily)在 Revit 里用于概念设计阶段,不是可维护的设备实体,运维系统不认。
- **族类别**:不同设备的正确族类别各异(机械设备 OST_MechanicalEquipment、管路附件 OST_PipeAccessories 等),只要不是 OST_Mass 即视为合规。

## 不合格判定

- 族类别是 `OST_Mass`(体量族) → 不合格
- 非 `FamilyInstance`(理论上有"设备编号"参数的非族实例元素) → 不合格

## 输出要求

返回匿名对象,字段:

- `DeviceCount`:设备总数
- `CompliantCount`:合格数
- `NonCompliantCount`:不合格数
- `MassFamilyCount`:用体量建的设备数
- `CategoryBreakdown`:按族类别汇总(族类别 → 设备数,前 20)
- `Issues`:不合格设备明细(前 50 个,字段:Id/Category/FamilyName/FamilyCategory/IsMassFamily/DeviceNumber/Issue 原因)

## 模板

`templates/DeviceFamilyCheck.cs` 实现 `IRevitDynamicCommand`,完成上述检查。遍历有"设备编号"参数的 FamilyInstance,检查 `Symbol.Family.FamilyCategory` 是否为 OST_Mass。可直接使用或按需扩展。
