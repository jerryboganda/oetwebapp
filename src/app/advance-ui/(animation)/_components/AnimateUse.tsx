import React from "react";
import { Col, Card, CardHeader, CardBody, Row } from "reactstrap";

const AnimateUse: React.FC = () => {
  return (
    <Col xs="12">
      <Card>
        <CardHeader>
          <h5>How to use</h5>
        </CardHeader>
        <CardBody>
          <div>
            <Row>
              <Col xs="12">
                <ol className="list-group list-group-numbered">
                  <li className="list-group-item d-flex justify-content-between align-items-start">
                    <div className="ms-2 me-auto">
                      <h6> By adding classes </h6>
                      <p>
                        Add the class <code>animate__animated</code> to an
                        element, along with any of the animation names forget
                        the <code>animate__</code> prefix!):{" "}
                      </p>
                      <p>
                        <code>
                          {`
                            <h5 class="animate__animated animate__bounce">An animated element</h5>
                          `}
                        </code>
                      </p>
                      <p className="fw-600 ms-3 mt-3">Utility Classes </p>
                      <div className="ms-4">
                        <p>Animate.css provides the following delays:</p>
                        <code>
                          animate__delay-2s, animate__delay-3s,
                          animate__delay-4s, animate__delay-5s
                        </code>
                        <p className="mt-2">
                          Slow, slower, fast, and Faster classes
                        </p>
                        <code>
                          animate__slow, animate__slower, animate__fast,
                          animate__faster
                        </code>
                        <p className="mt-2">Repeating classes</p>
                        <code>
                          animate__repeat-1, animate__repeat-2,
                          animate__repeat-3, animate__infinite
                        </code>
                      </div>
                    </div>
                  </li>
                  <li className="list-group-item d-flex justify-content-between align-items-start">
                    <div className="ms-2 me-auto">
                      <h6> Using @keyframes</h6>
                      <p>
                        Even though the library provides you a few helper
                        classes like the
                        <code>animated</code> class to get you up running
                        quickly, you can directly use the provided animations{" "}
                        <code>keyframes</code>. This provides a flexible way to
                        use Animate.css with your current projects without
                        having to refactor your HTML code.
                      </p>
                      <p className="fw-500">Example:</p>
                      <code className="d-flex flex-column">
                        <span>{`.my-element {`}</span>
                        <span>{`  display: inline-block;`}</span>
                        <span>{`  margin: 0 0.5rem;`}</span>
                        <span>{`  animation: bounce; /* referring directly to the animation's @keyframe declaration */`}</span>
                        <span>{`  animation-duration: 2s; /* don't forget to set a duration! */`}</span>
                        <span>{`}`}</span>
                      </code>
                    </div>
                  </li>
                  <li className="list-group-item d-sm-flex justify-content-between align-items-start">
                    <div className="ms-2 me-auto">
                      <h6> CSS Custom Properties (CSS Variables) </h6>
                      <p>
                        Animate.css uses custom properties (also known as CSS
                        variables) to define the animation&#39;s duration,
                        delay, delay, and iterations. This makes Animate.css
                        very flexible and customizable. Need to change an
                        animation duration? Just set a new value globally or
                        locally.
                      </p>
                      <p className="fw-500">Example:</p>
                      <p>
                        <code className="d-flex flex-column">
                          {`/* This only changes this particular animation duration */`}
                          <span>{`.animate__animated.animate__bounce {`}</span>
                          <span>{`  --animate-duration: 2s;`}</span>
                          <span>{`}`}</span>
                          <br />
                          {`/* This changes all the animations globally */`}
                          <span>{`:root {`}</span>
                          <span>{`  --animate-duration: 800ms;`}</span>
                          <span>{`  --animate-delay: 0.9s;`}</span>
                          <span>{`}`}</span>
                        </code>
                      </p>
                    </div>
                  </li>
                </ol>
              </Col>
              <div className="col-6"></div>
            </Row>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default AnimateUse;
