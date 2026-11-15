using ObsMuteWarning.Obs;

namespace ObsMuteWarning.Tests;

public class ObsConfigTests
{
    [Fact]
    public void AuthString_MatchesObsWebSocketAlgorithm()
    {
        // Expected value computed independently (Node.js crypto).
        Assert.Equal("sQBlPUYd9mki/3XVFBp4Pt08FCMWdMVIqnFWdEitUME=",
            ObsAuth.Compute("supersecret", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=",
                "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
    }

    [Fact]
    public void ReadsPasswordFromObsWebSocketConfig()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{ "alerts_enabled": false, "auth_required": true, "server_password": "abc123", "server_port": 4455 }""");
            Assert.Equal("abc123", ObsConfig.ReadObsWebSocketPassword(path));
            Assert.Null(ObsConfig.ReadObsWebSocketPassword(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_RoundTripWithPasswordEncrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"omw-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings
            {
                Obs = { Host = "10.0.0.2", Port = 4456, Password = "s3cret-pw" },
                Sources = { new WatchedSource { Name = "Mic", Uuid = "u1" } },
                Monitors = { @"\\.\DISPLAY2" },
                IcpEnabled = true,
                PopupPosition = PopupPosition.BottomRight,
                IcpBrightnessAfterWarning = 0,
            };
            s.Save(path);

            Assert.DoesNotContain("s3cret-pw", File.ReadAllText(path));
            var loaded = AppSettings.Load(path, out bool existed);
            Assert.True(existed);
            Assert.Equal("s3cret-pw", loaded.Obs.Password);
            Assert.Equal(4456, loaded.Obs.Port);
            Assert.Equal("u1", Assert.Single(loaded.Sources).Uuid);
            Assert.Equal(@"\\.\DISPLAY2", Assert.Single(loaded.Monitors));
            Assert.True(loaded.IcpEnabled);
            Assert.Equal(PopupPosition.BottomRight, loaded.PopupPosition);
            Assert.Equal((byte)0, loaded.IcpBrightnessAfterWarning);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_MissingFileGivesDefaultsWithoutSources()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"), out bool existed);
        Assert.False(existed);
        Assert.Empty(s.Sources);
        Assert.Empty(s.Monitors);
        Assert.False(s.IcpEnabled);
        Assert.Equal(4455, s.Obs.Port);
        Assert.Null(s.IcpBrightnessAfterWarning);
    }
}
