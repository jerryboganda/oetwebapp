"use client";
import React, { useRef, useState } from "react";
import { Card, Container, Col, Row } from "react-bootstrap";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import {
  faStar,
  faAngry,
  faFrown,
  faMeh,
  faSmile,
  faLaugh,
} from "@fortawesome/free-solid-svg-icons";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";
import SquareRating from "./SquareRating";
import PillRating from "./PillRating";
import RatingBar from "./RatingBar";
import ReversedRating from "./ReversedRating";
import HorizontalRating from "./HorizontalRating";

const Rating = () => {
  const [sliderValue, setSliderValue] = useState(5);
  const [activeRating, setActiveRating] = useState(0);
  const emojiContainerRef = useRef<HTMLDivElement>(null);
  const sliderInputRef = useRef<HTMLInputElement>(null);

  const emojis = ["😠", "😒", "😧", "😦", "😑", "😀", "😆", "😍", "🤩", "💙"];
  const textValues = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];
  const emojiIcons = [faAngry, faFrown, faMeh, faSmile, faLaugh];
  const colorArray = ["#F03161", "#74788D", "#F03161", "#FC9314", "#05BF81"];

  const handleSliderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const value = parseInt(e.target.value);
    setSliderValue(value);
  };

  const updateRating = (index: number) => {
    setActiveRating(index);
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Rating"
          title="Advance Ui"
          path={["Rating"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col md="3" xl="3">
            <Card className="equal-card">
              <Card.Header>
                <h5>Controlled</h5>
              </Card.Header>
              <Card.Body>
                <div className="rating">
                  {[5, 4, 3, 2, 1].map((value) => (
                    <React.Fragment key={`star${value}`}>
                      <input
                        type="radio"
                        id={`star${value}`}
                        name="rating"
                        value={value}
                        className="d-none"
                      />
                      <label className="star" htmlFor={`star${value}`}>
                        <FontAwesomeIcon icon={faStar} />
                      </label>
                    </React.Fragment>
                  ))}
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="3" xl="3">
            <Card className="equal-card">
              <Card.Header>
                <h5>Read Only</h5>
              </Card.Header>
              <Card.Body>
                <div className="rating">
                  {[1, 2, 3].map((i) => (
                    <FontAwesomeIcon
                      key={`filled-${i}`}
                      icon={faStar}
                      className="f-s-20 star-filled"
                    />
                  ))}
                  {[4, 5].map((i) => (
                    <FontAwesomeIcon
                      key={`warning-${i}`}
                      icon={faStar}
                      className="f-s-20 text-warning"
                    />
                  ))}
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="3" xl="3">
            <Card className="equal-card">
              <Card.Header>
                <h5>Custom SVG</h5>
              </Card.Header>
              <Card.Body>
                <div className="rating">
                  {[5, 4, 3, 2, 1].map((value) => (
                    <React.Fragment key={`star${value + 5}`}>
                      <input
                        type="radio"
                        id={`star${value + 5}`}
                        name="ratings3"
                        value={value}
                        className="d-none"
                      />
                      <label className="star" htmlFor={`star${value + 5}`}>
                        <FontAwesomeIcon icon={faStar} className="f-s-20" />
                      </label>
                    </React.Fragment>
                  ))}
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="3">
            <Card>
              <Card.Header>
                <h5>Full Star</h5>
              </Card.Header>
              <Card.Body>
                <div className="rating">
                  {[16, 17, 18, 19, 20].map((value) => (
                    <React.Fragment key={`star${value}`}>
                      <input
                        type="radio"
                        id={`star${value}`}
                        name="ratings"
                        value={value}
                        defaultChecked
                        className="d-none"
                      />
                      <label className="star" htmlFor={`star${value}`}>
                        <FontAwesomeIcon icon={faStar} />
                      </label>
                    </React.Fragment>
                  ))}
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card>
              <Card.Header>
                <h5>10 stars</h5>
              </Card.Header>
              <Card.Body>
                <div className="rating">
                  {Array.from({ length: 10 }).map((_, index) => {
                    const starId = `star${21 + index}`;
                    const starValue = 21 + index;
                    return (
                      <React.Fragment key={starId}>
                        <input
                          type="radio"
                          id={starId}
                          name="ratings4"
                          value={starValue}
                          defaultChecked={index === 0}
                          className="d-none"
                        />
                        <label className="star" htmlFor={starId}>
                          <FontAwesomeIcon icon={faStar} />
                        </label>
                      </React.Fragment>
                    );
                  })}
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card>
              <Card.Header>
                <h5>Square Rating</h5>
              </Card.Header>
              <Card.Body>
                <div className="boxs box-blue box-example-square d-flex-center">
                  <div className="box-body">
                    <SquareRating initial={1} />
                  </div>
                </div>
              </Card.Body>
            </Card>
          </Col>

          {/* Pill Rating */}
          <Col md="6" xxl="4">
            <Card>
              <Card.Header>
                <h5>Pill Rating</h5>
              </Card.Header>
              <Card.Body>
                <div className="boxs box-green box-example-pill d-flex-center">
                  <PillRating initial="C" />
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card>
              <Card.Header>
                <h5>1/10 Rating</h5>
              </Card.Header>
              <Card.Body>
                <div className="boxs box-orange box-example-1to10 h-75">
                  <RatingBar initial={7} />
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card>
              <Card.Header>
                <h5>Reversed Rating</h5>
              </Card.Header>
              <Card.Body>
                <div className="boxs box-green box-large box-example-reversed p-4">
                  <ReversedRating />
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card>
              <Card.Header>
                <h5>Hover Feedback</h5>
              </Card.Header>
              <Card.Body>
                <div className="stars_rating" id="stars_rating">
                  <div className="stars">
                    {[1, 2, 3, 4, 5].map((i) => (
                      <div
                        key={`hover-${i}`}
                        className={`stars1 ${i <= 3 ? "rated" : ""}`}
                      >
                        ★
                      </div>
                    ))}
                  </div>
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card className="equal-card">
              <Card.Header>
                <h5>Horizontal Rating</h5>
              </Card.Header>
              <Card.Body>
                <div className="boxs box-orange box-large box-example-horizontal d-flex-center">
                  <HorizontalRating max={10} initial={7} />
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card className="equal-card">
              <Card.Header>
                <h5>Emoji-Rating</h5>
              </Card.Header>
              <Card.Body>
                <div className="feedback-container">
                  <div className="emoji-container" ref={emojiContainerRef}>
                    {emojiIcons.map((icon, index) => (
                      <FontAwesomeIcon
                        key={`emoji-${index}`}
                        icon={icon}
                        className="fa-3x far"
                        style={{
                          transform: `translateX(-${activeRating * 47}px)`,
                          color:
                            index === activeRating
                              ? colorArray[index]
                              : "black",
                        }}
                      />
                    ))}
                  </div>
                  <div className="rating-container mt-4">
                    {[0, 1, 2, 3, 4].map((index) => (
                      <FontAwesomeIcon
                        key={`rating-${index}`}
                        icon={faStar}
                        className={`emoji-star ${index <= activeRating ? "active" : ""}`}
                        onClick={() => updateRating(index)}
                      />
                    ))}
                  </div>
                </div>
              </Card.Body>
            </Card>
          </Col>

          <Col md="6" xxl="4">
            <Card className="equal-card">
              <Card.Header>
                <h5>Emoji Progress</h5>
              </Card.Header>
              <Card.Body>
                <div className="rate-1">
                  <div className="emoji">{emojis[sliderValue]}</div>
                  <div className="emoji-slider">
                    <input
                      ref={sliderInputRef}
                      className="accent"
                      type="range"
                      min="0"
                      max="9"
                      step="1"
                      value={sliderValue}
                      onChange={handleSliderChange}
                    />
                  </div>
                  <label className="text">{textValues[sliderValue]}</label>
                </div>
              </Card.Body>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default Rating;
