#include <cassert>
#include <cmath>
#include <iostream>
#include "../../Firmware/RobotBridge/RobotBridge.ino"

void expect(const char* fragment) {
    if (Serial.output.str().find(fragment) == std::string::npos) {
        std::cerr << "Missing: " << fragment << "\nOutput:\n" << Serial.output.str();
        std::abort();
    }
}
void send(const std::string& text) {
    Serial.resetOutput();
    Serial.feed(text + "\n");
    while (Serial.available()) loop();
}
void advance(unsigned long milliseconds) {
    for (unsigned long elapsed = 0; elapsed < milliseconds; elapsed += 20) {
        fakeTime += 20;
        loop();
    }
}
bool near(float first, float second) { return std::fabs(first - second) < .002f; }

int main() {
    setup();
    assert(!armed && !attached);
    for (const auto& joint : joints) assert(!joint.attached() && joint.writeCount == 0);
    expect("BOOT AISENSOR 1");
    send("1 STEP 0 1"); expect("ERR 1 NOT_ARMED");
    send("2 ARM"); expect("DONE 2");
    assert(armed && attached);
    send("3 MOVE 47.4 110.5 43");
    expect("ACK 3"); assert(activeMotionId == 3);
    advance(100); expect("DONE 3");
    assert(near(currentAngle[0], 47.4f) && near(currentAngle[1], 110.5f));
    send("4 MOVE nan 110 43"); expect("ERR 4 BAD_NUMBER");
    send("5 MOVE 181 110 43"); expect("ERR 5 LIMIT");
    assert(near(currentAngle[0], 47.4f));
    send("6 STEP 1.5 1"); expect("ERR 6 BAD_STEP");
    send("7 STEP 0 2");
    advance(20);
    float atStop = currentAngle[0];
    send("8 STOP"); expect("ERR 7 CANCELLED"); expect("DONE 8");
    advance(100); assert(near(currentAngle[0], atStop));
    assert(armed && joints[0].attached());

    send("9 STEP 0 2");
    send("10 STEP 0 1"); expect("ERR 10 BUSY");
    fakeTime += WATCHDOG_MS + 1;
    loop(); expect("ERR 9 WATCHDOG"); expect("NOTICE WATCHDOG");
    assert(!armed && attached && near(currentAngle[0], atStop));
    for (int axis = 0; axis < 3; ++axis)
        assert(near(currentAngle[axis], targetAngle[axis]) && joints[axis].attached());

    send("11 ARM"); expect("DONE 11");
    assert(near(currentAngle[0], atStop)); // Повторное ARM не отправляет руку домой.
    send(std::string(600, 'X')); expect("LINE_TOO_LONG");
    send("12 STATUS"); expect("DONE 12");
    send("13 STEP 0 inf"); expect("BAD_STEP");
    send("14 STEP 0 0.1");
    advance(20); expect("DONE 14");
    assert(near(currentAngle[0], atStop + .1f)); // Дробный шаг не теряется.
    assert(joints[0].lastPulse >= PULSE_MIN_US && joints[0].lastPulse <= PULSE_MAX_US);

    Serial.feed("15 MOVE"); // Недописанная команда не блокирует watchdog.
    loop();
    fakeTime += WATCHDOG_MS + 1;
    loop();
    assert(!armed);
    Serial.feed("\n"); loop(); // Завершаем намеренно недописанную строку.
    send("16 ARM");
    send("17 STEP 0 1");
    send("HELLO"); expect("ERR 17 CANCELLED"); expect("READY AISENSOR 1");
    assert(!armed && !moving() && attached);
    std::cout << "PASS: boot inactive, ARM, fractional motion, bounds, invalid input, BUSY, STOP, watchdog, framing.\n";
}
