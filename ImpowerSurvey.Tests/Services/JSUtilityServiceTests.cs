using ImpowerSurvey.Services;
using Microsoft.JSInterop;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class JSUtilityServiceTests
    {
        private Mock<IJSRuntime> _mockJsRuntime;
        private JSUtilityService _jsUtilityService;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockJsRuntime = new Mock<IJSRuntime>();
            _jsUtilityService = new JSUtilityService(_mockJsRuntime.Object);
        }

        [TestMethod]
        public async Task ScrollToElement_CallsJsFunction()
        {
            // Arrange
            const string elementId = "testElement";
            
            // Act
            await _jsUtilityService.ScrollToElement(elementId);
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "scrollToElement",
                    It.Is<object[]>(args => args.Length == 1 && args[0].Equals(elementId))),
                Times.Once);
        }

        [TestMethod]
        public async Task ScrollToTop_CallsJsFunction()
        {
            // Act
            await _jsUtilityService.ScrollToTop();
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "scrollToTop",
                    It.Is<object[]>(args => args.Length == 0)),
                Times.Once);
        }

        [TestMethod]
        public async Task CopyToClipboard_CallsJsFunction()
        {
            // Arrange
            const string data = "testData";
            
            // Act
            await _jsUtilityService.CopyToClipboard(data);
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "navigator.clipboard.writeText",
                    It.Is<object[]>(args => args.Length == 1 && args[0].Equals(data))),
                Times.Once);
        }

        [TestMethod]
        public async Task DownloadHtmlFile_CallsJsEvalFunction()
        {
            // Arrange
            const string fileName = "test";
            const string fileType = "html";
            const string content = "<html><body>Test</body></html>";
            
            // Act
            await _jsUtilityService.DownloadHtmlFile(fileName, fileType, content);
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "eval", 
                    It.Is<object[]>(args => args.Length == 1 && args[0].ToString().Contains(content))),
                Times.Once);
        }

        [TestMethod]
        public async Task PreventTabClosure_CallsJsFunction()
        {
            // Arrange
            const string message = "Don't close";
            
            // Act
            await _jsUtilityService.PreventTabClosure(message);
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "preventWindowClose",
                    It.Is<object[]>(args => args.Length == 1 && args[0].Equals(message))),
                Times.Once);
        }

        [TestMethod]
        public async Task AllowTabClosure_CallsJsFunction()
        {
            // Act
            await _jsUtilityService.AllowTabClosure();
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "allowWindowClose",
                    It.Is<object[]>(args => args.Length == 0)),
                Times.Once);
        }

        [TestMethod]
        public async Task SetImpowerColors_CallsJsFunction()
        {
            // Act
            await _jsUtilityService.SetImpowerColors();
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "setThemeColors",
                    It.Is<object[]>(args => args.Length == 2 && 
                                        args[0].Equals("#096AF2") && 
                                        args[1].Equals("#F27A09"))),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateVantaForTheme_CallsJsFunction()
        {
            // Arrange
            const bool isDarkTheme = true;
            
            // Act
            await _jsUtilityService.UpdateVantaForTheme(isDarkTheme);
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "updateVantaForTheme",
                    It.Is<object[]>(args => args.Length == 1 && args[0].Equals(isDarkTheme))),
                Times.Once);
        }

        [TestMethod]
        public async Task GetTimezone_ReturnsJsResult()
        {
            // Arrange
            const string expectedTimezone = "America/New_York";
            _mockJsRuntime.Setup(js => js.InvokeAsync<string>(
                    It.Is<string>(s => s == "getTimezone"),
                    It.IsAny<object[]>()))
                .ReturnsAsync(expectedTimezone);
            
            // Act
            var result = await _jsUtilityService.GetTimezone();
            
            // Assert
            Assert.AreEqual(expectedTimezone, result);
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<string>("getTimezone", It.IsAny<object[]>()),
                Times.Once);
        }

        [TestMethod]
        public async Task EnableVantaBackground_CallsJsFunction()
        {
            // Act
            await _jsUtilityService.EnableVantaBackground(true);
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "initVantaBackground",
                    It.Is<object[]>(args => args.Length == 2 && 
                                          args[0].Equals("vanta-background") && 
                                          args[1] != null)),
                Times.Once);
        }

        [TestMethod]
        public async Task DisableVantaBackground_CallsJsFunction()
        {
            // Act
            await _jsUtilityService.DisableVantaBackground();
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "destroyVantaBackground",
                    It.Is<object[]>(args => args.Length == 0)),
                Times.Once);
        }

        [TestMethod]
        public async Task ApplyTabListStyle_CallsJsFunction()
        {
            // Arrange
            var mockJsRef = new Mock<IJSObjectReference>();
            _mockJsRuntime.Setup(js => js.InvokeAsync<IJSObjectReference>(
                    It.Is<string>(s => s == "document.querySelector"),
                    It.Is<object[]>(args => args.Length == 1 && args[0].Equals("ul[role='tablist']"))))
                .ReturnsAsync(mockJsRef.Object);
            
            // Act
            await _jsUtilityService.ApplyTabListStyle();
            
            // Assert
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<IJSObjectReference>(
                    "document.querySelector",
                    It.Is<object[]>(args => args.Length == 1 && args[0].Equals("ul[role='tablist']"))),
                Times.Once);
                
            _mockJsRuntime.Verify(
                js => js.InvokeAsync<object>(
                    "applyTablistStyle",
                    It.Is<object[]>(args => args.Length == 1 && args[0] == mockJsRef.Object)),
                Times.Once);
        }
    }
}