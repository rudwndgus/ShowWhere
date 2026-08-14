using System.Runtime.ExceptionServices;
using System.Threading;
using ShowWhere.Desktop;

namespace ShowWhere.Windows.Tests;

public sealed class AssistantUiIntegrationTests
{
    [Fact]
    public void Speech_bubble_opens_topmost_and_updates_from_pending_to_answer()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            AssistantSpeechBubbleWindow? bubble = null;
            try
            {
                var characters = new AssistantCharacterStore();
                bubble = new AssistantSpeechBubbleWindow(characters)
                {
                    BubbleText = "답변을 준비하고 있어요…",
                };
                bubble.PositionNear(500, 500, 52);
                bubble.Show();

                Assert.True(bubble.IsVisible);
                Assert.Equal("답변을 준비하고 있어요…", bubble.BubbleText);
                Assert.True(bubble.Topmost);
                bubble.BubbleText = "Cart 위치를 표시할게요.";
                Assert.Equal("Cart 위치를 표시할게요.", bubble.BubbleText);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                bubble?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Assistant UI test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
