// 単語カードの裏の画像 (Pixabay) のテスト。Program.cs の Main から呼ぶ。
using System;

public static partial class LogicTests
{
    private static void VocabImageTests()
    {
        string json = "{\"total\":2,\"hits\":[{\"id\":1,\"webformatURL\":\"https://pixabay.com/get/a_640.jpg\",\"previewURL\":\"p1\"}," +
                      "{\"id\":2,\"webformatURL\":\"https://pixabay.com/get/b_640.jpg\"}]}";
        Check(PixabayClient.PickImageUrl(json) == "https://pixabay.com/get/a_640.jpg", "pixabay pick: first hit");
        Check(PixabayClient.PickImageUrl("{\"total\":0,\"hits\":[]}") == null, "pixabay pick: no hits -> null");
        Check(PixabayClient.PickImageUrl("{\"hits\":[{\"id\":1}]}") == null, "pixabay pick: no url -> null");
        Check(PixabayClient.PickImageUrl("not json") == null && PixabayClient.PickImageUrl(null) == null, "pixabay pick: bad json -> null");

        string url = PixabayClient.SearchUrl("K&1", "look after");
        Check(url.StartsWith("https://pixabay.com/api/?key=K%261&q=look%20after"), "pixabay url: key and word escaped " + url);
        Check(url.Contains("safesearch=true") && url.Contains("lang=en"), "pixabay url: safe search, english");

        var now = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        Check(VocabImageStore.NoneIsFresh(now.AddDays(-29), now), "image none: 29 days -> still none");
        Check(!VocabImageStore.NoneIsFresh(now.AddDays(-31), now), "image none: 31 days -> ask again");
    }
}
