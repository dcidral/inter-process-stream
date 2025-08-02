using System.Reflection;

namespace InterProcessStream.Tests;

[TestClass]
public class TestSharedState
{
    public class TestCaseData
    {
        public TestCaseData(SharedState sharedState, int availableSpace, int unreadData)
        {
            SharedState = sharedState;
            AvailableSpace = (ulong)availableSpace;
            AvailableData = (ulong)unreadData;
        }

        public SharedState SharedState { get; }
        public ulong AvailableSpace { get; }
        public ulong AvailableData { get; }
    }

    public static IEnumerable<object[]> GetTestData()
    {
        yield return new object[] {
            new TestCaseData(
                new SharedState
                {
                    bufferSize = 10,
                    currentReaderIndex = 0,
                    currentWriterIndex = 0,
                },
                availableSpace: 9,
                unreadData: 0
            ),
            "Empty buffer"
        };

        yield return new object[] {
            new TestCaseData(
                new SharedState
                {
                    bufferSize = 10,
                    currentReaderIndex = 5,
                    currentWriterIndex = 5,
                },
                availableSpace: 9,
                unreadData: 0
            ),
            "Buffer with no data to read"
        };

        yield return new object[] {
            new TestCaseData(
                new SharedState
                {
                    bufferSize = 10,
                    currentReaderIndex = 0,
                    currentWriterIndex = 5,
                },
                availableSpace: 4,
                unreadData: 5
            ),
            "Unread data"
        };

        yield return new object[] {
            new TestCaseData(
                new SharedState
                {
                    bufferSize = 10,
                    currentReaderIndex = 5,
                    currentWriterIndex = 0,
                },
                availableSpace: 4,
                unreadData: 5
            ),
            "Unread data with reader position > writer position"
        };
    }

    public static string GetCustomDisplayName(MethodInfo methodInfo, object[] data)
    {
        return $"{methodInfo.Name} - {data.Last()}";
    }

    /// <summary>
    /// Test the available space to write.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(GetTestData), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(GetCustomDisplayName))]
    public void TestAvailableSpace(
        TestCaseData testCase,
        string _ // case name for custom display name
    )
    {
        var expectedAvailability = testCase.AvailableSpace;
        var sharedState = testCase.SharedState;
        Assert.AreEqual(expectedAvailability, sharedState.GetAvailableSpace());
    }

    /// <summary>
    /// Test the available data to read.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(GetTestData), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(GetCustomDisplayName))]
    public void TestAvailableData(
        TestCaseData testCase,
        string _ // case name for custom display name
    )
    {
        var expectedAvailableData = testCase.AvailableData;
        var sharedState = testCase.SharedState;
        Assert.AreEqual(expectedAvailableData, sharedState.GetAvailableData());
    }

    [TestMethod]
    public void TestDefaultState()
    {
        SharedState sharedState = new SharedState();
        Assert.IsFalse(sharedState.IsWriterConnected);
        Assert.IsFalse(sharedState.IsReaderConnected);
        Assert.IsFalse(sharedState.IsStreamFinished);
    }
}