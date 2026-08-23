using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitAgent.DynamicCode;

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
            EnclosedRoomCount = rooms.Count(room => room.IsEnclosed),
            Rooms = rooms.Take(20).ToList()
        };
    }
}
