// iOS haptics backend for HapticFeedbackService (UIImpactFeedbackGenerator).
// NOT VERIFIED: written without an iOS build environment or device.
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *gVowLight = nil;
static UIImpactFeedbackGenerator *gVowHeavy = nil;

extern "C" {

void VowHapticPrepare(void)
{
    if (gVowLight == nil) gVowLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    if (gVowHeavy == nil) gVowHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
    [gVowLight prepare];
    [gVowHeavy prepare];
}

void VowHapticImpact(int heavy)
{
    UIImpactFeedbackGenerator *generator = heavy ? gVowHeavy : gVowLight;
    if (generator == nil) return;
    [generator impactOccurred];
    [generator prepare];
}

}
