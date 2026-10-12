// SpeechRecognizerBridge.mm
// Unity から SFSpeechRecognizer を使うための最小ブリッジ。
// 配置場所: Assets/Plugins/iOS/SpeechRecognizerBridge.mm

#import <Foundation/Foundation.h>
#import <Speech/Speech.h>
#import <AVFoundation/AVFoundation.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString*                              gObjectName   = nil;
static SFSpeechRecognizer*                    gRecognizer   = nil;
static AVAudioEngine*                         gEngine       = nil;
static SFSpeechAudioBufferRecognitionRequest* gRequest      = nil;
static SFSpeechRecognitionTask*               gTask         = nil;
static BOOL                                   gTapInstalled = NO;
static BOOL                                   gEnded        = YES;
static NSInteger                              gSession      = 0;

static void Send(const char* method, NSString* message)
{
    if (gObjectName == nil) return;
    UnitySendMessage([gObjectName UTF8String], method, message != nil ? [message UTF8String] : "");
}

static void StopAudio()
{
    if (gEngine != nil)
    {
        if (gEngine.isRunning) [gEngine stop];
        if (gTapInstalled)
        {
            [gEngine.inputNode removeTapOnBus:0];
            gTapInstalled = NO;
        }
    }
}

// 現在のセッションを破棄する (Unity へは通知しない)
static void HardReset()
{
    gEnded = YES;
    gSession++;
    StopAudio();
    if (gTask != nil) { [gTask cancel]; gTask = nil; }
    gRequest = nil;
    gEngine  = nil;
}

static NSLocale* LocaleFrom(const char* tag)
{
    NSString* s = tag != NULL ? [NSString stringWithUTF8String:tag] : @"en-US";
    return [[NSLocale alloc] initWithLocaleIdentifier:s];
}

extern "C"
{
    void _SR_Init(const char* objectName)
    {
        gObjectName = [NSString stringWithUTF8String:objectName];
    }

    // 1: 利用可能 / 0: 不可
    int _SR_IsAvailable(const char* languageTag)
    {
        SFSpeechRecognizer* r = [[SFSpeechRecognizer alloc] initWithLocale:LocaleFrom(languageTag)];
        return (r != nil && r.isAvailable) ? 1 : 0;
    }

    void _SR_RequestPermission()
    {
        [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {
            BOOL ok = (status == SFSpeechRecognizerAuthorizationStatusAuthorized);
            dispatch_async(dispatch_get_main_queue(), ^{
                Send("OnNativePermissionResult", ok ? @"1" : @"0");
            });
        }];
    }

    void _SR_Start(const char* languageTag)
    {
        NSLocale* locale = LocaleFrom(languageTag);

        dispatch_async(dispatch_get_main_queue(), ^{
            HardReset();

            gRecognizer = [[SFSpeechRecognizer alloc] initWithLocale:locale];
            if (gRecognizer == nil || !gRecognizer.isAvailable)
            {
                Send("OnNativeError", @"Speech recognizer is not available.");
                return;
            }

            NSError* error = nil;
            AVAudioSession* session = [AVAudioSession sharedInstance];
            [session setCategory:AVAudioSessionCategoryRecord
                            mode:AVAudioSessionModeMeasurement
                         options:AVAudioSessionCategoryOptionDuckOthers
                           error:&error];
            [session setActive:YES withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation error:&error];
            if (error != nil)
            {
                Send("OnNativeError", error.localizedDescription);
                return;
            }

            gEnded   = NO;
            gSession++;
            NSInteger sid = gSession;

            gRequest = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
            gRequest.shouldReportPartialResults = YES;

            gEngine = [[AVAudioEngine alloc] init];
            AVAudioInputNode* input = gEngine.inputNode;
            AVAudioFormat* format = [input outputFormatForBus:0];
            if (format.sampleRate <= 0)
            {
                HardReset();
                Send("OnNativeError", @"Invalid audio input format (no microphone?).");
                return;
            }

            SFSpeechAudioBufferRecognitionRequest* request = gRequest;
            [input installTapOnBus:0
                        bufferSize:1024
                            format:format
                             block:^(AVAudioPCMBuffer* buffer, AVAudioTime* when) {
                [request appendAudioPCMBuffer:buffer];
            }];
            gTapInstalled = YES;

            [gEngine prepare];
            if (![gEngine startAndReturnError:&error])
            {
                HardReset();
                Send("OnNativeError", error.localizedDescription);
                return;
            }

            gTask = [gRecognizer recognitionTaskWithRequest:gRequest
                                              resultHandler:^(SFSpeechRecognitionResult* result, NSError* err) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (sid != gSession || gEnded) return;

                    if (result != nil)
                    {
                        NSString* text = result.bestTranscription.formattedString;
                        if (text == nil) text = @"";

                        if (result.isFinal)
                        {
                            gEnded = YES;
                            StopAudio();
                            gTask = nil; gRequest = nil; gEngine = nil;
                            Send("OnNativeFinal", text);
                            return;
                        }
                        Send("OnNativePartial", text);
                    }

                    if (err != nil)
                    {
                        gEnded = YES;
                        StopAudio();
                        gTask = nil; gRequest = nil; gEngine = nil;
                        // 1110: "No speech detected" → 何も聞き取れなかった扱い (空の最終結果)
                        if (err.code == 1110) Send("OnNativeFinal", @"");
                        else                  Send("OnNativeError", err.localizedDescription);
                    }
                });
            }];
        });
    }

    // 話し終わり。音声入力を止めて endAudio → 最終結果が resultHandler に届く
    void _SR_Stop()
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (gEnded) return;
            StopAudio();
            [gRequest endAudio];
        });
    }

    void _SR_Cancel()
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            HardReset();
        });
    }
}
