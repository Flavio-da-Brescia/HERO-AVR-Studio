#include <Arduino.h>
#define LIB_HEADER <HeroAlpha.h>
#include LIB_HEADER
#if 0
#include <NotInstalledAndNotNeeded.h>
#endif
struct Counter { uint8_t value; };
Counter counter = {0};
// bogus function void never() { }
void setup() {
 pinMode(13, OUTPUT);
 counter = nextCounter(counter);
 digitalWrite(13, counter.value);
}
void loop() { delay(500); }
