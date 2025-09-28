// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class RequestReply_Tests
{
    [TestMethod]
    public void Default()
    {
        RequestReply requestReply = default;

        Assert.AreEqual(0u, requestReply.Seqnum);
#pragma warning disable MSTEST0025 // Use 'Assert.Fail' instead of an always-failing assert
        Assert.IsNull(requestReply.Bytes);
#pragma warning restore MSTEST0025 // Use 'Assert.Fail' instead of an always-failing assert
    }

    [TestMethod]
    public void Constructor()
    {
        var requestReply = new RequestReply(42, [1, 2, 3]);

        Assert.AreEqual(42u, requestReply.Seqnum);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, requestReply.Bytes);
    }
}
