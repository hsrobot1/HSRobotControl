import time
import board
import pulseio
import digitalio
from adafruit_motor import servo
import supervisor

led = digitalio.DigitalInOut(board.D13)
led.direction = digitalio.Direction.OUTPUT

switchDown = digitalio.DigitalInOut(board.D3)
switchDown.direction = digitalio.Direction.INPUT
switchDown.pull = digitalio.Pull.UP

switchUp = digitalio.DigitalInOut(board.D4)
switchUp.direction = digitalio.Direction.INPUT
switchUp.pull = digitalio.Pull.UP

pwm1 = pulseio.PWMOut(board.A2, duty_cycle=2 ** 15, frequency=50)
pwm2 = pulseio.PWMOut(board.A1, duty_cycle=2 ** 15, frequency=50)

left_servo = servo.Servo(pwm1, min_pulse=750, max_pulse=2250)
right_servo = servo.Servo(pwm2, min_pulse=750, max_pulse=2250)

left_servo.actuation_range = 180.0
right_servo.actuation_range = 180.0
angle_up = 25.0
angle_down = 25.0
interval = 0.01
updownchange = 0.05
updowncount = 500.0
updownsleep = 0.05
horizontal = 90.0
left_servo_offset = 0.0
right_servo_offset = -1.0
left_servo.angle = horizontal + left_servo_offset
right_servo.angle = horizontal + right_servo_offset

while True:
    if supervisor.runtime.serial_bytes_available:
            value = input()
            
            if value == "":
                continue
            else:
                print(value)
                values = value.split(" ")
                if len(values) == 3:
                    if value[0] >= value[1] and value[0] <= value[2]:
                        minangle = horizontal - angle_down
                        maxangle = horizontal + angle_up
                        differenceangle = maxangle - minangle
                        mindistance = float(values[1])
                        maxdistance = float(values[2])
                        differencedistance = maxdistance - mindistance
                        distance = float(values[0])
                        ratiodistance = (distance - mindistance) / differencedistance
                        angle = minangle + differenceangle * ratiodistance
                        print(angle)
                        if angle >= minangle and angle <= maxangle:
                            left_servo.angle = angle + left_servo_offset
                            right_servo.angle = 180 - angle + right_servo_offset
                            if not switchUp.value:
                                updowncount += updowncount * updownchange
                                time.sleep(updownsleep)
                            if not switchDown.value:
                                updowncount -= updowncount * updownchange
                                time.sleep(updownsleep)