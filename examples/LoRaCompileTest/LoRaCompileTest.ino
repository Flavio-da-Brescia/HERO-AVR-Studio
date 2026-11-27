#include <SPI.h>
#include <Wire.h>
#include <EEPROM.h>
#include <SX1262.h>
SX1262 radio;
loRa_Para_t cfg={434500000UL,16,LORA_SF9,LORA_BW_500,LORA_CR_4_5,200};
void setup(){Serial.begin(38400);Wire.begin();SPI.begin();Serial.println(EEPROM.read(0));Serial.println(radio.Init(&cfg));}
void loop(){}
