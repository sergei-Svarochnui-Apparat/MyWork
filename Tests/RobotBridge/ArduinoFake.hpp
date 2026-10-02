#pragma once
#include <algorithm>
#include <deque>
#include <sstream>
#include <string>
#include <iomanip>
using std::min;
inline unsigned long fakeTime = 0;
inline unsigned long millis() { return fakeTime; }
struct FakeSerial {
    std::deque<char> input;
    std::ostringstream output;
    void begin(int) {}
    int available() const { return static_cast<int>(input.size()); }
    char read() { char ch = input.front(); input.pop_front(); return ch; }
    template<class T> void print(T value) { output << value; }
    void print(float value, int digits) {
        auto flags = output.flags(); auto precision = output.precision();
        output << std::fixed << std::setprecision(digits) << value;
        output.flags(flags); output.precision(precision);
    }
    template<class T> void println(T value) { print(value); println(); }
    void println() { output << '\n'; }
    void resetOutput() { output.str(""); output.clear(); }
    void feed(const std::string& text) { for (char ch : text) input.push_back(ch); }
};
inline FakeSerial Serial;
