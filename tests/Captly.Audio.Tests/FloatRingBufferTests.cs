namespace Captly.Audio.Tests;

public class FloatRingBufferTests
{
    [Fact]
    public void ReadsBackWhatWasWritten()
    {
        var ring = new FloatRingBuffer(8);
        ring.Write([1, 2, 3]);

        var output = new float[8];
        Assert.Equal(3, ring.Read(output));
        Assert.Equal([1f, 2f, 3f], output[..3]);
        Assert.Equal(0, ring.Available);
    }

    [Fact]
    public void WrapsAroundTheEnd()
    {
        var ring = new FloatRingBuffer(4);
        ring.Write([1, 2, 3]);
        ring.Read(new float[3]);
        ring.Write([4, 5, 6]);

        var output = new float[3];
        ring.Read(output);
        Assert.Equal([4f, 5f, 6f], output);
    }

    [Fact]
    public void WhenFull_KeepsTheOldestAndReportsTheShortfall()
    {
        var ring = new FloatRingBuffer(3);
        Assert.Equal(3, ring.Write([1, 2, 3, 4, 5]));
        Assert.Equal(0, ring.Write([6]));

        var output = new float[3];
        ring.Read(output);
        Assert.Equal([1f, 2f, 3f], output);
    }

    [Fact]
    public void ReadsOnlyWhatFits()
    {
        var ring = new FloatRingBuffer(8);
        ring.Write([1, 2, 3]);

        Assert.Equal(2, ring.Read(new float[2]));
        Assert.Equal(1, ring.Available);
    }

    [Fact]
    public async Task OneWriterAndOneReader_LoseAndReorderNothing()
    {
        const int total = 2_000_000;
        var ring = new FloatRingBuffer(1024);

        var writer = Task.Run(() =>
        {
            var chunk = new float[97];
            for (var sent = 0; sent < total;)
            {
                var size = Math.Min(chunk.Length, total - sent);
                for (var i = 0; i < size; i++) chunk[i] = sent + i;
                var stored = ring.Write(chunk.AsSpan(0, size));
                if (stored < size)
                {
                    // Fill the block again from where the buffer stopped taking it.
                    size = stored;
                }
                sent += size;
            }
        }, TestContext.Current.CancellationToken);

        var output = new float[131];
        for (var received = 0; received < total;)
        {
            var count = ring.Read(output);
            for (var i = 0; i < count; i++)
                Assert.Equal((float)(received + i), output[i]);
            received += count;
        }

        await writer;
    }
}
