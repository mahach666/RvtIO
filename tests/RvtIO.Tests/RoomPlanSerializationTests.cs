using System.Text;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class RoomPlanSerializationTests
{
    private const uint Plan = 200;

    [Fact]
    public void Plan_ReadsTheRoomsTheCircuitAreasAndTheComponents()
    {
        byte[] block = Block();
        Assert.True(TfBlockDeserializer.TryExtractExactRoomPlan(block, 0, Size(block), 2306, out var plan));
        Assert.Equal([7, 8], plan!.Rooms);
        Assert.Equal(new Dictionary<int, double> { [10] = 100, [11] = -20, [12] = 50 }, plan.CircuitAreas);
        Assert.Equal([(10, -1), (11, 10), (12, -1)], plan.Components);
    }

    [Fact]
    public void RoomArea_IsTheCircuitLessItsIslands()
    {
        byte[] block = Block();
        Assert.True(TfBlockDeserializer.TryExtractExactRoomPlan(block, 0, Size(block), 2306, out var plan));
        // Circuit 11 runs the other way round and is the outer circuit of the island circuit 10 encloses.
        Assert.Equal(80, plan!.RoomArea(10));
        Assert.Equal(50, plan.RoomArea(12));
        Assert.Equal(-20, plan.RoomArea(11));
        Assert.Null(plan.RoomArea(99));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("class")]
    [InlineData("footer")]
    [InlineData("size")]
    [InlineData("truncated")]
    [InlineData("plan-id")]
    [InlineData("duplicate-circuit")]
    [InlineData("unknown-member")]
    [InlineData("unknown-outer")]
    [InlineData("unknown-enclosing")]
    [InlineData("duplicate-room")]
    [InlineData("huge-count")]
    [InlineData("no-topology")]
    public void Plan_RejectsInvalidFramingOrStructure(string mutation)
    {
        byte[] block = Block(mutation);
        switch (mutation)
        {
            case "owner": BitConverter.GetBytes(300u).CopyTo(block, 0); break;
            case "class": BitConverter.GetBytes((ushort)2980).CopyTo(block, 12); break;
            case "footer": block[^1] ^= 1; break;
            case "size": block[8] ^= 1; break;
            case "truncated": block = block[..^1]; break;
        }
        Assert.False(TfBlockDeserializer.TryExtractExactRoomPlan(block, 0, Size(block), 2306, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void Recorder_DuplicateOrMalformedRootCannotBeOverwrittenByLaterSuccess()
    {
        byte[] block = Block();
        var state = new ParsingState();
        RvtFileReader.RecordRoomPlanSource(state, block, 0, Size(block), 2306, Plan);
        Assert.Equal(1, state.RoomPlanSource[Plan].BlockCount);
        Assert.NotNull(state.RoomPlanSource[Plan].Plan);
        RvtFileReader.RecordRoomPlanSource(state, block, 0, Size(block), 2306, Plan);
        Assert.Equal(2, state.RoomPlanSource[Plan].BlockCount);
        Assert.Null(state.RoomPlanSource[Plan].Plan);
        var malformed = new ParsingState();
        RvtFileReader.RecordRoomPlanSource(malformed, block[..^1], 0, Size(block), 2306, Plan);
        RvtFileReader.RecordRoomPlanSource(malformed, block, 0, Size(block), 2306, Plan);
        Assert.Null(malformed.RoomPlanSource[Plan].Plan);
    }

    [Fact]
    public void Builder_GivesARoomItsPlanAreaOnlyWhenOnePlanHoldsItWithAValidatedCircuit()
    {
        byte[] block = Block();
        var state = new ParsingState();
        RvtFileReader.RecordRoomPlanSource(state, block, 0, Size(block), 2306, Plan);
        // Another plan that lists room 8 as well makes its plan ambiguous.
        byte[] second = Block(rooms: [8, 9], owner: 201);
        RvtFileReader.RecordRoomPlanSource(state, second, 0, Size(second), 2306, 201);
        foreach (uint room in new uint[] { 7, 8, 9, 10 }) state.RoomNameSource[room] = (1, "Room");
        state.RoomNameSource[6] = (2, null);
        state.ExactRoomCircuitIds[7] = 10; // a circuit with an island
        state.ExactRoomCircuitIds[8] = 12;
        state.ExactRoomCircuitIds[9] = 12;
        state.ExactRoomCircuitIds[10] = 12; // held by no plan
        state.ExactRoomCircuitIds[6] = 12;

        var rooms = new[] { 6, 7, 8, 9, 10 }.Select(id => new Element { Id = id, CategoryId = -2000160 }).ToArray();
        var notARoom = new Element { Id = 7, CategoryId = -2000011 };
        var info = new RevitFileInfo { Elements = [.. rooms, notARoom] };
        ElementBuilder.ApplySerializedRoomPlanAreas(info, state);

        Assert.Null(rooms[0].SerializedRoomPlanAreaSquareFeet);   // two blocks of the room owner
        Assert.Equal(80, rooms[1].SerializedRoomPlanAreaSquareFeet);
        Assert.Null(rooms[2].SerializedRoomPlanAreaSquareFeet);   // room 8 is in two plans
        Assert.Equal(50, rooms[3].SerializedRoomPlanAreaSquareFeet);
        Assert.Null(rooms[4].SerializedRoomPlanAreaSquareFeet);   // room 10 is in no plan
        Assert.Null(notARoom.SerializedRoomPlanAreaSquareFeet);
    }

    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);

    private static byte[] Block(string? mutation = null, int[]? rooms = null, uint owner = Plan)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(owner); writer.Write(0u); writer.Write(0u); writer.Write((ushort)2306);
        foreach (var field in TfBlockDeserializer.SerializationFields(2306)!)
        {
            switch (field.Name)
            {
                case "m_id" when field.Declaring == 37: writer.Write((int)owner); continue;
                case "m_planTopology":
                    if (mutation == "no-topology") writer.Write(0u);
                    else { writer.Write(1u); writer.Write((ushort)2980); }
                    continue;
            }
            if ((field.Mods >> 4) == 5) { writer.Write(0u); continue; }
            switch (field.Type)
            {
                case 1: writer.Write((byte)0); break;
                case 4: writer.Write(0); break;
                case 7: writer.Write(0d); break;
                case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
        if (mutation != "no-topology") WriteTopology(writer, mutation, rooms ?? [7, 8], owner);
        writer.Write(0u);
        byte[] block = stream.ToArray();
        uint size = (uint)block.Length - 16;
        BitConverter.GetBytes(size).CopyTo(block, 8);
        BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
        return block;
    }

    /// <summary>PlanTopology: offsets, level, plan id, next circuit, flag, segments, circuits, components, rooms, then the unused
    /// items and the seed cache (both empty).</summary>
    private static void WriteTopology(BinaryWriter writer, string? mutation, int[] rooms, uint owner)
    {
        writer.Write(0d); writer.Write(10d); writer.Write(-1d);
        writer.Write(0); writer.Write(5); writer.Write(mutation == "plan-id" ? 999 : (int)owner); writer.Write(13);
        writer.Write((byte)1);

        writer.Write(2u);
        for (int segment = 0; segment < 2; segment++)
        {
            for (int part = 0; part < 4; part++) writer.Write(segment * 4 + part); // DirectedSegmentKey
            writer.Write(0d); writer.Write(1d);
            for (int part = 0; part < 4; part++) writer.Write((double)part); // m_envelope
            writer.Write(3d); writer.Write(1.5d);
            writer.Write(2u);
            for (int point = 0; point < 4; point++) writer.Write((double)point);
        }

        (int Id, double Area)[] circuits = [(10, 100), (11, -20), (mutation == "duplicate-circuit" ? 10 : 12, 50)];
        writer.Write(mutation == "huge-count" ? 0x0FFFFFFFu : (uint)circuits.Length);
        foreach (var (id, area) in circuits)
        {
            writer.Write(area);
            for (int part = 0; part < 4; part++) writer.Write((double)part); // m_envelope
            writer.Write(id);
            writer.Write(1u);
            for (int part = 0; part < 4; part++) writer.Write(part); // one side
        }

        writer.Write(3u);
        WriteComponent(writer, [mutation == "unknown-member" ? 77 : 10], mutation == "unknown-outer" ? 78 : 10, -1);
        WriteComponent(writer, [11], 11, mutation == "unknown-enclosing" ? 79 : 10);
        WriteComponent(writer, [12], 12, -1);

        int[] listed = mutation == "duplicate-room" ? [7, 7] : rooms;
        writer.Write((uint)listed.Length);
        foreach (int room in listed) writer.Write(room);
        writer.Write(0u); // m_unusedItems
        writer.Write(0u); // m_rbSeedCache
    }

    private static void WriteComponent(BinaryWriter writer, int[] members, int outer, int enclosing)
    {
        writer.Write((uint)members.Length);
        foreach (int member in members) writer.Write(member);
        writer.Write(outer);
        writer.Write(enclosing);
    }
}
