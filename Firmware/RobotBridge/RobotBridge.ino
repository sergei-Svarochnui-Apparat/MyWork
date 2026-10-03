#include <ESP32Servo.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

// Три известных привода: основание, плечо, локоть.
// Окна расширены на 25° в каждую сторону по запросу пользователя.
// Это программные ограничения, НЕ измеренные пределы механики.
constexpr int PINS[3] = {32, 33, 25};
constexpr float HOME[3] = {47.0f, 110.0f, 43.0f};
constexpr float MIN_ANGLE[3] = {12.0f, 80.0f, 13.0f};
constexpr float MAX_ANGLE[3] = {82.0f, 140.0f, 73.0f};
constexpr float SPEED_DEG_PER_SECOND = 15.0f;
constexpr unsigned long WATCHDOG_MS = 1500;
constexpr int PULSE_MIN_US = 544;
constexpr int PULSE_MAX_US = 2400;

Servo joints[3];
float currentAngle[3] = {47, 110, 43};
float targetAngle[3] = {47, 110, 43};
bool attached = false;
bool armed = false;
long activeMotionId = -1;
constexpr int AUX_PIN = 26;
constexpr int AUX_PULSE_MIN_US = 1000, AUX_PULSE_MAX_US = 2000;
Servo auxiliary;
float auxAngle = 90, auxTarget = 90, auxSpeed = 60;
bool auxAttached = false, auxEnabled = false;
long auxMotionId = -1;
unsigned long auxLastUpdate = 0, auxLastState = 0;
unsigned long lastContact = 0, lastUpdate = 0, lastState = 0;
char inputLine[128];
size_t inputLength = 0;
bool inputOverflow = false;

bool moving() {
  for (int i = 0; i < 3; ++i)
    if (fabsf(targetAngle[i] - currentAngle[i]) > 0.001f) return true;
  return false;
}

bool auxMoving() { return fabsf(auxTarget - auxAngle) > 0.001f; }

void auxiliaryState() {
  Serial.print("AUXSTATE 26 "); Serial.print(auxEnabled ? 1 : 0); Serial.print(' ');
  Serial.print(auxAttached ? 1 : 0); Serial.print(' ');
  Serial.print(auxMoving() ? 1 : 0); Serial.print(' ');
  Serial.print(auxAngle, 3); Serial.print(' '); Serial.print(auxTarget, 3); Serial.print(' ');
  Serial.println(auxSpeed, 3);
  auxLastState = millis();
}

void writeAuxiliary() {
  auxiliary.writeMicroseconds(lroundf(AUX_PULSE_MIN_US +
    auxAngle * (AUX_PULSE_MAX_US - AUX_PULSE_MIN_US) / 180.0f));
}

void reply(const char* kind, long id, const char* message = nullptr) {
  Serial.print(kind); Serial.print(' '); Serial.print(id);
  if (message) { Serial.print(' '); Serial.print(message); }
  Serial.println();
}

void state() {
  Serial.print("STATE "); Serial.print(armed ? 1 : 0); Serial.print(' ');
  Serial.print(moving() ? 1 : 0);
  for (int i = 0; i < 3; ++i) { Serial.print(' '); Serial.print(currentAngle[i], 3); }
  for (int i = 0; i < 3; ++i) {
    Serial.print(' '); Serial.print(MIN_ANGLE[i], 3);
    Serial.print(' '); Serial.print(MAX_ANGLE[i], 3);
  }
  Serial.println();
  lastState = millis();
}

void writeJoint(int axis) {
  int pulse = lroundf(PULSE_MIN_US +
    currentAngle[axis] * (PULSE_MAX_US - PULSE_MIN_US) / 180.0f);
  joints[axis].writeMicroseconds(pulse);
}

void stopMotion(const char* reason) {
  if (activeMotionId >= 0) reply("ERR", activeMotionId, reason);
  activeMotionId = -1;
  for (int i = 0; i < 3; ++i) targetAngle[i] = currentAngle[i];
  // PWM сохраняется: STOP не снимает удерживающий момент с плеча.
}

void stopAuxiliary(const char* reason) {
  if (auxMotionId >= 0) reply("ERR", auxMotionId, reason);
  auxMotionId = -1;
  auxTarget = auxAngle;
}

bool number(const char* token, float& result) {
  if (!token || !*token) return false;
  char* end = nullptr;
  result = strtof(token, &end);
  return *end == '\0' && isfinite(result);
}

void command(char* line) {
  char* tokens[8];
  int count = 0;
  char* context = nullptr;
  char* token = strtok_r(line, " \t", &context);
  while (token && count < 8) {
    tokens[count++] = token;
    token = strtok_r(nullptr, " \t", &context);
  }
  if (token || count == 0) { if (token) Serial.println("ERR 0 BAD_LINE"); return; }
  if (count == 1 && strcmp(tokens[0], "PING") == 0) {
    lastContact = millis(); return;
  }
  if ((count == 1 || count == 2) && strcmp(tokens[0], "HELLO") == 0) {
    lastContact = millis();
    stopMotion("CANCELLED");
    stopAuxiliary("CANCELLED");
    armed = false; // Каждый новый сеанс требует явного ARM.
    auxEnabled = false;
    Serial.print("READY AISENSOR 1");
    if (count == 2) { Serial.print(' '); Serial.print(tokens[1]); }
    Serial.println(); state(); auxiliaryState(); return;
  }

  char* end = nullptr;
  long id = strtol(tokens[0], &end, 10);
  if (end == tokens[0] || *end != '\0' || id <= 0 || count < 2) {
    Serial.println("ERR 0 BAD_ID"); return;
  }
  const char* action = tokens[1];

  if (strcmp(action, "STOP") == 0 && count == 2) {
    lastContact = millis();
    stopMotion("CANCELLED");
    stopAuxiliary("CANCELLED");
    reply("ACK", id); state(); auxiliaryState(); reply("DONE", id); return;
  }
  if (strcmp(action, "STATUS") == 0 && count == 2) {
    lastContact = millis();
    reply("ACK", id); state(); auxiliaryState(); reply("DONE", id); return;
  }
  // D26 работает отдельно: его движение не занимает очередь трёх суставов руки.
  if ((strcmp(action, "AUXOFF") == 0 || strcmp(action, "AUXSTOP") == 0) && count == 2) {
    lastContact = millis();
    stopAuxiliary("CANCELLED");
    if (strcmp(action, "AUXOFF") == 0) {
      if (auxAttached) auxiliary.detach();
      auxAttached = auxEnabled = false;
    }
    reply("ACK", id); auxiliaryState(); reply("DONE", id); return;
  }
  if ((strcmp(action, "AUXON") == 0 || strcmp(action, "AUXMOVE") == 0) && count == 4) {
    float angle, speed;
    if (!number(tokens[2], angle) || !number(tokens[3], speed)) {
      reply("ERR", id, "BAD_NUMBER"); return;
    }
    if (angle < 0 || angle > 180 || speed < 15 || speed > 180) {
      reply("ERR", id, "LIMIT"); return;
    }
    if (auxMotionId >= 0) { reply("ERR", id, "BUSY"); return; }
    if (strcmp(action, "AUXON") == 0) {
      if (!auxAttached) {
        auxiliary.setPeriodHertz(50);
        auxiliary.setTimerWidth(16);
        auxiliary.attach(AUX_PIN, AUX_PULSE_MIN_US, AUX_PULSE_MAX_US);
        if (!auxiliary.attached()) { reply("ERR", id, "ATTACH_FAILED"); return; }
        auxAttached = true;
        auxAngle = angle; // Фактическая поза неизвестна; первое включение явно задаёт угол.
        writeAuxiliary();
      }
      auxEnabled = true;
    } else if (!auxEnabled) { reply("ERR", id, "AUX_NOT_ENABLED"); return; }
    lastContact = auxLastUpdate = millis();
    auxTarget = angle;
    auxSpeed = speed;
    reply("ACK", id);
    if (auxMoving()) auxMotionId = id;
    auxiliaryState();
    if (!auxMoving()) reply("DONE", id);
    return;
  }
  if (strcmp(action, "ARM") == 0 && count == 2) {
    lastContact = millis();
    stopMotion("CANCELLED");
    if (!attached) {
      // При первом включении фактическое положение неизвестно: будет задана HOME.
      // Поддержи руку и поставь её близко к этой позе перед нажатием «Включить».
      for (int i = 0; i < 3; ++i) {
        joints[i].setPeriodHertz(50);
        joints[i].setTimerWidth(16);
        joints[i].attach(PINS[i], PULSE_MIN_US, PULSE_MAX_US);
        if (!joints[i].attached()) {
          for (int j = 0; j <= i; ++j) joints[j].detach();
          reply("ERR", id, "ATTACH_FAILED"); return;
        }
        writeJoint(i);
      }
      attached = true;
    }
    armed = true;
    lastUpdate = millis();
    reply("ACK", id); state(); reply("DONE", id); return;
  }

  float destination[3];
  for (int i = 0; i < 3; ++i) destination[i] = currentAngle[i];
  if (strcmp(action, "HOME") == 0 && count == 2) {
    for (int i = 0; i < 3; ++i) destination[i] = HOME[i];
  } else if (strcmp(action, "MOVE") == 0 && count == 5) {
    for (int i = 0; i < 3; ++i)
      if (!number(tokens[i + 2], destination[i])) {
        reply("ERR", id, "BAD_NUMBER"); return;
      }
  } else if (strcmp(action, "STEP") == 0 && count == 4) {
    float axisValue, delta;
    if (!number(tokens[2], axisValue) || !number(tokens[3], delta) ||
        axisValue < 0 || axisValue > 2 || axisValue != floorf(axisValue) ||
        fabsf(delta) > 2.0f) {
      reply("ERR", id, "BAD_STEP"); return;
    }
    destination[(int)axisValue] += delta;
  } else {
    reply("ERR", id, "BAD_COMMAND"); return;
  }

  if (!armed) { reply("ERR", id, "NOT_ARMED"); return; }
  if (activeMotionId >= 0) { reply("ERR", id, "BUSY"); return; }
  for (int i = 0; i < 3; ++i)
    if (destination[i] < MIN_ANGLE[i] || destination[i] > MAX_ANGLE[i]) {
      reply("ERR", id, "LIMIT"); return;
    }

  lastContact = millis();
  for (int i = 0; i < 3; ++i) targetAngle[i] = destination[i];
  reply("ACK", id);
  if (moving()) activeMotionId = id;
  else { state(); reply("DONE", id); }
}

void updateMotion(unsigned long now) {
  if (!armed || now - lastUpdate < 20) return;
  float elapsed = min((unsigned long)(now - lastUpdate), 100UL) / 1000.0f;
  lastUpdate = now;
  float maximumChange = SPEED_DEG_PER_SECOND * elapsed;
  for (int i = 0; i < 3; ++i) {
    float difference = targetAngle[i] - currentAngle[i];
    if (fabsf(difference) <= 0.001f) continue;
    currentAngle[i] += copysignf(fminf(fabsf(difference), maximumChange), difference);
    writeJoint(i);
  }
  if (activeMotionId >= 0 && !moving()) {
    long completedId = activeMotionId;
    activeMotionId = -1;
    state(); reply("DONE", completedId);
  }
}

void updateAuxiliary(unsigned long now) {
  if (!auxEnabled || now - auxLastUpdate < 20) return;
  float elapsed = min((unsigned long)(now - auxLastUpdate), 100UL) / 1000.0f;
  auxLastUpdate = now;
  float difference = auxTarget - auxAngle;
  if (fabsf(difference) > 0.001f) {
    auxAngle += copysignf(fminf(fabsf(difference), auxSpeed * elapsed), difference);
    writeAuxiliary();
  }
  if (auxMotionId >= 0 && !auxMoving()) {
    long completedId = auxMotionId;
    auxMotionId = -1;
    auxiliaryState(); reply("DONE", completedId);
  }
}

void setup() {
  Serial.begin(115200);
  lastContact = lastUpdate = millis();
  Serial.println("BOOT AISENSOR 1");
  state();
  auxiliaryState();
  // Приводы пока не подключены: автоматического движения при загрузке нет.
}

void loop() {
  // Ограниченный объём чтения, чтобы поток команд не вытеснил STOP/движение/watchdog.
  int budget = 128;
  while (Serial.available() && budget-- > 0) {
    char ch = (char)Serial.read();
    if (ch == '\r') continue;
    if (ch == '\n') {
      if (inputOverflow) Serial.println("ERR 0 LINE_TOO_LONG");
      else { inputLine[inputLength] = '\0'; command(inputLine); }
      inputLength = 0; inputOverflow = false;
    } else if (!inputOverflow) {
      if (inputLength + 1 < sizeof(inputLine)) inputLine[inputLength++] = ch;
      else inputOverflow = true;
    }
  }
  unsigned long now = millis();
  if ((armed || auxEnabled) && now - lastContact > WATCHDOG_MS) {
    stopMotion("WATCHDOG");
    stopAuxiliary("WATCHDOG");
    armed = false;
    auxEnabled = false;
    Serial.println("NOTICE WATCHDOG");
    state();
    auxiliaryState();
  }
  updateMotion(now);
  updateAuxiliary(now);
  if (now - lastState >= 250) state();
  if (now - auxLastState >= 250) auxiliaryState();
}
