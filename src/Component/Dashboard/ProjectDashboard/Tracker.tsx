import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  Col,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownToggle,
} from "reactstrap";
import {
  ChatLines,
  Instagram,
  MoreHoriz,
  PlaySolid,
  Refresh,
  ShareAndroidSolid,
  Square,
  Twitter,
} from "iconoir-react";

const Tracker = () => {
  const [elapsedTime, setElapsedTime] = useState<number>(0);
  const [timer, setTimer] = useState<ReturnType<typeof setInterval> | null>(
    null
  );
  const [history, setHistory] = useState([
    {
      session: "Session 1",
      time: "00:01:23",
      className: "bg-info-300 text-info-dark",
    },
    {
      session: "Session 2",
      time: "00:02:45",
      className: "bg-primary-300 text-primary-dark",
    },
    {
      session: "Session 3",
      time: "00:03:30",
      className: "bg-danger-300 text-danger-dark",
    },
    {
      session: "Session 4",
      time: "00:04:12",
      className: "bg-warning-300 text-warning-dark",
    },
    {
      session: "Session 5",
      time: "01:06:00",
      className: "bg-success-300 text-success-dark",
    },
  ]);
  const [historyCount, setHistoryCount] = useState(history.length);
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const toggleDropdown = () => setDropdownOpen(!dropdownOpen);

  const formatTime = (ms: string | number | Date) =>
    new Date(ms).toISOString().slice(11, 19);

  const startTimer = () => {
    const startTime = Date.now() - elapsedTime;
    const newTimer = setInterval(() => {
      setElapsedTime(Date.now() - startTime);
    }, 100);
    setTimer(newTimer);
  };

  const stopTimer = () => {
    if (timer) {
      clearInterval(timer as ReturnType<typeof setInterval>);
    }
    setTimer(null);
    const newHistory = {
      session: `Session ${historyCount + 1}`,
      time: formatTime(elapsedTime),
      className: "bg-info-300 text-info-dark",
    };
    setHistory([...history, newHistory]);
    setHistoryCount(historyCount + 1);
  };

  const resetTimer = () => {
    if (timer) {
      clearInterval(timer as ReturnType<typeof setInterval>);
    }
    setTimer(null);
    setElapsedTime(0);
  };
  return (
    <Col md="6" xxl="3">
      <div className="p-3">
        <h5>Tracker</h5>
      </div>
      <Card>
        <CardBody className="position-relative">
          <div className="time-tracker text-center">
            <div className="share-time mb-3">
              <Dropdown isOpen={dropdownOpen} toggle={toggleDropdown}>
                <DropdownToggle
                  tag="span"
                  className="w-35 h-35 bg-primary-300 text-info-dark rounded p-2 d-flex align-items-center justify-content-center"
                  role="button"
                >
                  <ShareAndroidSolid
                    height={24}
                    width={24}
                    className="text-primary"
                  />
                </DropdownToggle>
                <DropdownMenu className="dropdown-menu-end rounded">
                  <DropdownItem>
                    <Instagram
                      height={18}
                      width={18}
                      className="text-danger-dark me-2 f-s-18 align-text-top"
                    />
                    Instagram
                  </DropdownItem>
                  <DropdownItem>
                    <Twitter
                      height={18}
                      width={18}
                      className="text-twitter me-2 f-s-18 align-text-top"
                    />
                    Twitter
                  </DropdownItem>
                  <DropdownItem>
                    <ChatLines
                      height={18}
                      width={18}
                      className="text-whatsapp me-2 f-s-18 align-text-top"
                    />
                    Messenger
                  </DropdownItem>
                  <DropdownItem>
                    <MoreHoriz
                      height={18}
                      width={18}
                      className="text-dark me-2 f-s-18 align-text-top"
                    />
                    Other Apps
                  </DropdownItem>
                </DropdownMenu>
              </Dropdown>
            </div>
            <h1 className="timer-display f-w-600">{formatTime(elapsedTime)}</h1>
            <div className="controls d-flex justify-content-center mt-3">
              <Button
                className="btn btn-light-primary icon-btn b-r-18 me-2"
                onClick={startTimer}
                disabled={!!timer}
                id="start-btn"
              >
                <PlaySolid height={18} width={18} />
              </Button>
              <Button
                className="btn btn-danger icon-btn b-r-18 me-2"
                onClick={stopTimer}
                disabled={!timer}
                id="stop-btn"
              >
                <Square height={18} width={18} />
              </Button>
              <Button
                className="btn btn-light-info icon-btn b-r-18"
                onClick={resetTimer}
                id="reset-btn"
              >
                <Refresh height={18} width={18} />
              </Button>
            </div>
          </div>
          <ul className="tracker-history-list app-scroll mt-3">
            {history.map((item, index) => (
              <li className={item.className} key={index}>
                <div>
                  <h6 className={`${item.className.split(" ")[1]} mb-0`}>
                    {item.session}
                  </h6>
                </div>
                <div className="text-dark f-w-600 ms-2">{item.time}</div>
              </li>
            ))}
          </ul>
        </CardBody>
      </Card>
    </Col>
  );
};

export default Tracker;
