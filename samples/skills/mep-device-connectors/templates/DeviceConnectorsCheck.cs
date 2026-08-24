using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.DynamicCode;

// mep-device-connectors 技能模板:机电设备连接器合规检查。
// 检查项:设备是否有 connector 系统、有 connector、connector 是否已连接(未连接=问题)。
// 用 var 推断 MEPModel/ConnectorManager 类型(避免 namespace 不一致),try/catch 兼容非 MEP 类别。

public sealed class DynamicCommand : IRevitDynamicCommand
{
    private const string DeviceNumberParamName = "设备编号";

    private class DeviceInfo
    {
        public int Id;
        public string Category;
        public string FamilyName;
        public string DeviceNumber;
        public bool HasConnectorSystem;
        public int TotalConnectors;
        public int ConnectedCount;
        public int UnconnectedCount;
    }

    public object Execute(Document document)
    {
        // 筛选有"设备编号"参数的族实例
        var devices = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(inst => inst.GetParameters(DeviceNumberParamName).Count > 0
                        || (inst.Symbol != null && inst.Symbol.GetParameters(DeviceNumberParamName).Count > 0))
            .ToList();

        var deviceInfos = new List<DeviceInfo>();
        foreach (var inst in devices)
        {
            var info = new DeviceInfo
            {
                Id = inst.Id.IntegerValue,
                Category = inst.Category == null ? string.Empty : inst.Category.Name,
                FamilyName = inst.Symbol == null ? string.Empty : inst.Symbol.Family.Name
            };

            // 取设备编号(用于显示)
            var numberParams = inst.GetParameters(DeviceNumberParamName);
            if (numberParams.Count > 0)
            {
                var v = numberParams[0].AsString();
                if (string.IsNullOrWhiteSpace(v)) v = numberParams[0].AsValueString();
                info.DeviceNumber = v ?? string.Empty;
            }

            // 获取 ConnectorManager(MEP 类别的设备才有)
            try
            {
                var mepModel = inst.MEPModel;
                if (mepModel != null)
                {
                    var cm = mepModel.ConnectorManager;
                    if (cm != null)
                    {
                        info.HasConnectorSystem = true;
                        foreach (Connector conn in cm.Connectors)
                        {
                            info.TotalConnectors++;
                            if (conn.IsConnected)
                                info.ConnectedCount++;
                            else
                                info.UnconnectedCount++;
                        }
                    }
                }
            }
            catch { /* 非 MEP 类别,无 MEPModel */ }

            deviceInfos.Add(info);
        }

        // 判定 + issues
        var issues = new List<object>();
        int compliant = 0;
        foreach (var d in deviceInfos)
        {
            var reasons = new List<string>();
            if (!d.HasConnectorSystem)
                reasons.Add("无 connector 系统(非 MEP 设备或无连接器)");
            else if (d.TotalConnectors == 0)
                reasons.Add("无 connector");
            else if (d.UnconnectedCount > 0)
                reasons.Add("有 " + d.UnconnectedCount + " 个未连接的 connector");
            if (reasons.Count == 0)
                compliant++;
            else
                issues.Add(new
                {
                    d.Id,
                    d.Category,
                    d.FamilyName,
                    d.DeviceNumber,
                    d.HasConnectorSystem,
                    d.TotalConnectors,
                    d.ConnectedCount,
                    d.UnconnectedCount,
                    Issue = string.Join("、", reasons)
                });
        }

        return new
        {
            DeviceCount = devices.Count,
            CompliantCount = compliant,
            NonCompliantCount = issues.Count,
            NoConnectorSystemCount = deviceInfos.Count(d => !d.HasConnectorSystem),
            DevicesWithUnconnectedCount = deviceInfos.Count(d => d.UnconnectedCount > 0),
            TotalConnectors = deviceInfos.Sum(d => d.TotalConnectors),
            TotalConnected = deviceInfos.Sum(d => d.ConnectedCount),
            TotalUnconnected = deviceInfos.Sum(d => d.UnconnectedCount),
            Issues = issues.Take(50).ToList()
        };
    }
}
