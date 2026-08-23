using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitAgent.DynamicCode;

// room-audit 技能模板：审计房间放置/封闭情况与面积。
// 可直接使用，或在此基础上按楼层汇总、筛选特定房间后扩展。

public sealed class DynamicCommand : IRevitDynamicCommand
{
    public object Execute(Document document)
    {
        var rooms = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Select(room => new
            {
                Id = room.Id.IntegerValue,
                room.Number,
                room.Name,
                LevelName = document.GetElement(room.LevelId) == null ? string.Empty : document.GetElement(room.LevelId).Name,
                room.Area,
                IsPlaced = room.Location != null,
                IsEnclosed = room.Area > 0
            })
            .ToList();

        return new
        {
            RoomCount = rooms.Count,
            PlacedRoomCount = rooms.Count(r => r.IsPlaced),
            UnplacedRoomCount = rooms.Count(r => !r.IsPlaced),
            EnclosedRoomCount = rooms.Count(r => r.IsEnclosed),
            UnenclosedRoomCount = rooms.Count(r => !r.IsEnclosed),
            Rooms = rooms.Take(20).ToList()
        };
    }
}
