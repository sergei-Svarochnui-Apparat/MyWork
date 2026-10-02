#pragma once
#include "ArduinoFake.hpp"
class Servo {
    bool enabled = false;
public:
    int pin = -1, lastPulse = 0, writeCount = 0;
    void setPeriodHertz(int) {}
    void setTimerWidth(int) {}
    void attach(int nextPin, int, int) { pin = nextPin; enabled = true; }
    bool attached() const { return enabled; }
    void detach() { enabled = false; }
    void writeMicroseconds(int pulse) { lastPulse = pulse; writeCount++; }
};
