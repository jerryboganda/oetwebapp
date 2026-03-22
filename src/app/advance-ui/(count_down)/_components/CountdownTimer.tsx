"use client";
import React, { useEffect, useState } from "react";

interface CountdownTimerProps {
  targetDate: string;
  showCountDownCircle?: boolean;
  showCountDownBg?: boolean;
  showDays?: boolean;
  showHours?: boolean;
  showMinutes?: boolean;
  showSeconds?: boolean;
}

const CountdownTimer: React.FC<CountdownTimerProps> = ({
  targetDate,
  showCountDownCircle = false,
  showCountDownBg = false,
  showDays = true,
  showHours = true,
  showMinutes = true,
  showSeconds = true,
}) => {
  const [timeLeft, setTimeLeft] = useState({
    days: 0,
    hours: 0,
    minutes: 0,
    seconds: 0,
  });

  useEffect(() => {
    const countDownDate = new Date(targetDate).getTime();

    const updateTimer = () => {
      const now = new Date().getTime();
      const distance = countDownDate - now;

      if (distance < 0) {
        clearInterval(timerId);
        return;
      }

      setTimeLeft({
        days: Math.floor(distance / (1000 * 60 * 60 * 24)),
        hours: Math.floor(
          (distance % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60)
        ),
        minutes: Math.floor((distance % (1000 * 60 * 60)) / (1000 * 60)),
        seconds: Math.floor((distance % (1000 * 60)) / 1000),
      });
    };

    const timerId = setInterval(updateTimer, 1000);
    updateTimer();

    return () => clearInterval(timerId);
  }, [targetDate]);

  return (
    <ul className="timer">
      {showCountDownBg && (
        <>
          <li className="countdown">
            <span className="days f-s-20 fw-bold">{timeLeft.days}</span>
            <span className="day">Day</span>
          </li>
          <li className="countdown">
            <span className="hours f-s-20 fw-bold">{timeLeft.hours}</span>
            <span className="hour">Hour</span>
          </li>
          <li className="countdown">
            <span className="minutes f-s-20 fw-bold">{timeLeft.minutes}</span>
            <span className="min">Min</span>
          </li>
          <li className="countdown">
            <span className="seconds f-s-20 fw-bold">{timeLeft.seconds}</span>
            <span className="sec">Sec</span>
          </li>
        </>
      )}
      {showCountDownCircle && (
        <>
          <li className="countdown">
            <span className="days fw-medium f-s-20 bg-light-primary">
              {timeLeft.days}
            </span>
            <span className="timer-countdown bg-primary">Day</span>
          </li>
          <li className="countdown">
            <span className="hours fw-medium f-s-20 bg-light-secondary">
              {timeLeft.hours}
            </span>
            <span className="timer-countdown bg-secondary">Hour</span>
          </li>
          <li className="countdown">
            <span className="minutes fw-medium f-s-20 bg-light-success">
              {timeLeft.minutes}
            </span>
            <span className="timer-countdown bg-success">Minutes</span>
          </li>
          <li className="countdown">
            <span className="seconds fw-medium f-s-20 bg-light-info">
              {timeLeft.seconds}
            </span>
            <span className="timer-countdown bg-info">Seconds</span>
          </li>
        </>
      )}
      {showDays && (
        <>
          <li className="countdown">
            <h6 className="days mb-0 f-s-20 fw-bold">{timeLeft.days}</h6>
            <p className="timer-countdown">Days</p>
          </li>
          <li className="countdown">
            <h6 className="hours mb-0 f-s-20 fw-bold">{timeLeft.hours}</h6>
            <p className="timer-countdown">Hours</p>
          </li>
          <li className="countdown">
            <h6 className="minutes mb-0 f-s-20 fw-bold">{timeLeft.minutes}</h6>
            <p className="timer-countdown">Min</p>
          </li>
          <li className="countdown">
            <h6 className="seconds mb-0 f-s-20 fw-bold">{timeLeft.seconds}</h6>
            <p className="timer-countdown">Sec</p>
          </li>
        </>
      )}
      {showHours && (
        <>
          <li className="app-countdown countdown-border">
            <span className="Hours f-s-20 fw-bold">{timeLeft.hours}</span>
            <span className="timer-countdown">Hours</span>
          </li>
          <li className="app-countdown">
            <span className="minutes f-s-20 fw-bold">{timeLeft.minutes}</span>
            <span className="timer-countdown">Minutes</span>
          </li>
          <li className="app-countdown countdown-border-1">
            <span className="seconds f-s-20 fw-bold">{timeLeft.seconds}</span>
            <span className="timer-countdown">Seconds</span>
          </li>
        </>
      )}
      {showMinutes && (
        <>
          <li className="countdown">
            <span className="minutes time-value fw-bold">
              {timeLeft.minutes}
            </span>
            <span className="timer-countdown f-s-14 f-w-400">Minutes </span>
          </li>
          <li className="app-line">:</li>
          <li className="countdown">
            <span className="seconds fw-bold">{timeLeft.seconds}</span>
            <span className="timer-countdown f-s-14 f-w-400">Seconds</span>
          </li>
        </>
      )}
      {showSeconds && (
        <>
          <li className="seconds fw-bold">{timeLeft.seconds}</li>
          <li className="timer-countdown">seconds</li>
        </>
      )}
    </ul>
  );
};

export default CountdownTimer;
