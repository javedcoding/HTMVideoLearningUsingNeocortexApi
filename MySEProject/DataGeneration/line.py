import cv2
import numpy as np
from cv2 import VideoWriter, VideoWriter_fourcc
import math
import os

screenWidth = 120
screenHeight = 120
FPS = 24
seconds = 4
thickness = 1
r = 20

Y = screenHeight - r  - 5
X = r + 10

color = (0, 0, 0)

vectorLength = 10
Angle = -30
vectorAngle = math.radians(Angle) #-- range 0 -> 359 degree on geometric angle--
x = int(math.cos(vectorAngle)*vectorLength)
y = -int(math.sin(vectorAngle)*vectorLength)
vectorTransform = { 'x': x, 'y': y}

experimentName = "R"+str(r)+"_Angle"+str(Angle)+"_Speed"+str(vectorLength)
fileName = experimentName+str("/")
if(not(os.path.exists(experimentName))):
    try:
        os.mkdir(experimentName)
    except OSError:
        print(OSError)

fourcc = VideoWriter_fourcc(*'MP42')
video = VideoWriter('./'+experimentName+'/line.mp4', -1, float(FPS), (screenWidth, screenHeight))

for i in range(FPS*seconds):
    frame = np.zeros((screenHeight,screenWidth,3),np.uint8)
    frame[:,:] = [255, 255, 255]
    # Draw a solid blue circle in the center
    if(((X+r+vectorTransform['x'])>screenWidth) or ((X-r+vectorTransform['x'])<0)):
        vectorTransform['x'] = -vectorTransform['x']
            
    if(((Y+r+vectorTransform['y'])>screenHeight) or ((Y-r+vectorTransform['y'])<0)):
        vectorTransform['y'] = -vectorTransform['y']
    X+=vectorTransform['x']
    Y+=vectorTransform['y']
    cv2.line(frame, (X, Y), (X, Y-20), color, thickness)
    video.write(frame)

video.release()