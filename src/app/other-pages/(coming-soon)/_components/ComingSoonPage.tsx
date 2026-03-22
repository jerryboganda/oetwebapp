"use client";
import React, { useState, useEffect } from "react";
import { Container, Row, Col, Input } from "reactstrap";

const ComingSoonPage = () => {
  const [timeLeft, setTimeLeft] = useState({
    days: 0,
    hours: 0,
    minutes: 0,
    seconds: 0,
  });

  useEffect(() => {
    const deadlineDate = new Date(2026, 11, 31, 23, 59, 59).getTime();

    const interval = setInterval(() => {
      const currentDate = new Date().getTime();
      const distance = deadlineDate - currentDate;

      if (distance < 0) {
        clearInterval(interval);
      } else {
        const days = Math.floor(distance / (1000 * 60 * 60 * 24));
        const hours = Math.floor(
          (distance % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60)
        );
        const minutes = Math.floor((distance % (1000 * 60 * 60)) / (1000 * 60));
        const seconds = Math.floor((distance % (1000 * 60)) / 1000);

        setTimeLeft({ days, hours, minutes, seconds });
      }
    }, 1000);

    return () => clearInterval(interval);
  }, []);

  return (
    <div>
      <Container fluid>
        <Row>
          <Col xs="12" className="p-0">
            <div className="coming-soon">
              <div className="coundown-timmer p-5">
                <div className="content text-center">
                  <h2 className="text-dark">PolytronX Coming Soon</h2>
                </div>
                <div className="timmer-content d-flex justify-content-center align-items-center gap-3 mt-4">
                  <div className="numbers text-center">
                    <span id="days" className="display-4">
                      {timeLeft.days}
                    </span>
                    <span>Days</span>
                  </div>
                  <span>:</span>
                  <div className="numbers text-center">
                    <span id="hours" className="display-4">
                      {timeLeft.hours}
                    </span>
                    <span>Hours</span>
                  </div>
                  <span>:</span>
                  <div className="numbers text-center">
                    <span id="minutes" className="display-4">
                      {timeLeft.minutes}
                    </span>
                    <span>Minutes</span>
                  </div>
                  <span>:</span>
                  <div className="numbers text-center">
                    <span id="seconds" className="display-4">
                      {timeLeft.seconds}
                    </span>
                    <span>Seconds</span>
                  </div>
                </div>

                <p className="font-coming-p mt-4 f-w-500">
                  Get notified when PolytronX launches:
                </p>
                <div className="app-form mb-3 mt-2 rounded-control d-flex align-items-center flex-wrap gap-2 justify-content-center">
                  <Input
                    className="form-control-lg text-center coming-soon-input"
                    id="username"
                    placeholder="Enter an Email"
                    type="email"
                  />
                  <a className="btn btn-light-primary btn-xl" href="#">
                    Subscribe
                  </a>
                </div>

                <div className="copy-right-section mt-3 text-center">
                  <p className="f-w-500 mb-0 f-s-18">
                    Copyright © 2025 PolytronX. All rights reserved{" "}
                    <a className="f-w-600 text-primary-dark" href="#">
                      V1.0.0
                    </a>
                  </p>
                </div>
              </div>
            </div>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ComingSoonPage;
