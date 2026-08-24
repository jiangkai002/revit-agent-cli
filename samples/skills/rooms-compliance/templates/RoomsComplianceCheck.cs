using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitAgent.DynamicCode;

// rooms-compliance 技能模板:建筑运维房间合规检查。
// 检查项:放置、封闭、房间编号参数存在、参数值非空、编号值唯一。
// 两轮:先收集每房间数据 + 编号值,再按重复集合判定合格。

public sealed class DynamicCommand : IRevitDynamicCommand
{
    private const string NumberParamName = "房间编号";

    private class RoomInfo
    {
        public int Id;
        public string Number;
        public string Name;
        public string LevelName;
        public double Area;
        public bool IsPlaced;
        public bool IsEnclosed;
        public bool HasNumberParam;
        public string NumberValue;
        public bool HasValue;
    }

    public object Execute(Document document)
    {
        var rooms = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .ToList();

        // 第一轮:收集每房间检查数据
        var roomInfos = rooms.Select(room =>
        {
            var numberParams = room.GetParameters(NumberParamName);
            var hasNumberParam = numberParams.Count > 0;
            string numberValue = null;
            if (hasNumberParam)
            {
                numberValue = numberParams[0].AsString();
                if (string.IsNullOrWhiteSpace(numberValue))
                    numberValue = numberParams[0].AsValueString();
            }
            var levelEl = document.GetElement(room.LevelId);
            return new RoomInfo
            {
                Id = room.Id.IntegerValue,
                Number = room.Number,
                Name = room.Name,
                LevelName = levelEl == null ? string.Empty : levelEl.Name,
                Area = room.Area,
                IsPlaced = room.Location != null,
                IsEnclosed = room.Area > 0,
                HasNumberParam = hasNumberParam,
                NumberValue = numberValue ?? string.Empty,
                HasValue = !string.IsNullOrWhiteSpace(numberValue)
            };
        }).ToList();

        // 编号值 → 房间数(用于查重)
        var numberGroups = roomInfos
            .Where(r => r.HasValue)
            .GroupBy(r => r.NumberValue)
            .ToDictionary(g => g.Key, g => g.Count());
        var duplicateNumbers = new HashSet<string>(
            numberGroups.Where(kv => kv.Value > 1).Select(kv => kv.Key));

        // 第二轮:判定合格 + 收集 issues
        var issues = new List<object>();
        int compliant = 0;
        foreach (var r in roomInfos)
        {
            var reasons = new List<string>();
            if (!r.IsPlaced) reasons.Add("未放置");
            if (!r.IsEnclosed) reasons.Add("未封闭");
            if (!r.HasNumberParam) reasons.Add("缺'房间编号'参数");
            if (r.HasNumberParam && !r.HasValue) reasons.Add("编号值为空");
            if (r.HasValue && duplicateNumbers.Contains(r.NumberValue)) reasons.Add("编号重复");

            if (reasons.Count == 0)
                compliant++;
            else
                issues.Add(new
                {
                    r.Id,
                    r.Number,
                    r.Name,
                    r.LevelName,
                    r.Area,
                    r.IsPlaced,
                    r.IsEnclosed,
                    r.HasNumberParam,
                    r.NumberValue,
                    Issue = string.Join("、", reasons)
                });
        }

        var duplicateGroups = numberGroups
            .Where(kv => kv.Value > 1)
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => new { Number = kv.Key, RoomCount = kv.Value })
            .ToList();

        var duplicateRoomCount = roomInfos
            .Count(r => r.HasValue && duplicateNumbers.Contains(r.NumberValue));

        return new
        {
            RoomCount = rooms.Count,
            CompliantCount = compliant,
            NonCompliantCount = issues.Count,
            UnplacedCount = roomInfos.Count(r => !r.IsPlaced),
            UnenclosedCount = roomInfos.Count(r => !r.IsEnclosed),
            MissingNumberParamCount = roomInfos.Count(r => !r.HasNumberParam),
            EmptyNumberValueCount = roomInfos.Count(r => r.HasNumberParam && !r.HasValue),
            DuplicateNumberCount = duplicateRoomCount,
            DuplicateGroups = duplicateGroups,
            Issues = issues.Take(50).ToList()
        };
    }
}
