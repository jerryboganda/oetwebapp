import { Col, Row, Card, CardHeader, CardBody } from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import React, { useState } from "react";

const LoadingButtons = () => {
  const [isCodeVisible, setIsCodeVisible] = useState(false);
  return (
    <Col xs={12}>
      <Card className="button-view">
        <CardHeader className="code-header">
          <h5>Loading Buttons</h5>
          <a
            href="#loadingbtnexample"
            onClick={(e) => {
              e.preventDefault();
              setIsCodeVisible(!isCodeVisible);
            }}
          >
            <IconCode data-source="loadingbtn" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <Row>
            <Col lg={6} xs={12} className="app-btn-list mb-0">
              <button
                className="btn btn-primary d-inline-flex-center"
                type="button"
              >
                <span
                  className="spinner-border spinner-border-sm me-2"
                  role="status"
                  aria-hidden="true"
                />
                Loading...
              </button>
              <button
                className="btn btn-secondary d-inline-flex-center"
                type="button"
              >
                Wait...{" "}
                <span
                  className="spinner-border spinner-border-sm ms-2"
                  role="status"
                  aria-hidden="true"
                />
              </button>
              <button className="btn btn-success icon-btn" type="button">
                <span
                  className="spinner-border spinner-border-sm"
                  role="status"
                  aria-hidden="true"
                />
                <span className="visually-hidden">Loading...</span>
              </button>
              <button className="btn btn-danger icon-btn" type="button">
                <span
                  className="spinner-grow spinner-grow-sm"
                  role="status"
                  aria-hidden="true"
                />
                <span className="visually-hidden">Loading...</span>
              </button>
            </Col>
            <Col lg={6} xs={12} className="app-btn-list">
              <button
                className="btn btn-outline-primary d-inline-flex-center"
                type="button"
              >
                <span
                  className="spinner-border spinner-border-sm me-2"
                  role="status"
                  aria-hidden="true"
                />
                Loading...
              </button>
              <button
                className="btn btn-outline-secondary d-inline-flex-center"
                type="button"
              >
                Wait...{" "}
                <span
                  className="spinner-border spinner-border-sm ms-2"
                  role="status"
                  aria-hidden="true"
                />
              </button>
              <button
                className="btn btn-outline-success icon-btn"
                type="button"
              >
                <span
                  className="spinner-border spinner-border-sm"
                  role="status"
                  aria-hidden="true"
                ></span>
                <span className="visually-hidden">Loading...</span>
              </button>
              <button className="btn btn-outline-danger icon-btn" type="button">
                <span
                  className="spinner-grow spinner-grow-sm"
                  role="status"
                  aria-hidden="true"
                />
                <span className="visually-hidden">Loading...</span>
              </button>
            </Col>
            <div className="col-lg-6 col-12 app-btn-list">
              <button
                className="btn btn-light-primary d-inline-flex-center"
                type="button"
              >
                <span
                  className="spinner-border spinner-border-sm me-2"
                  role="status"
                  aria-hidden="true"
                />
                Loading...
              </button>
              <button
                className="btn btn-light-secondary d-inline-flex-center"
                type="button"
              >
                Wait...{" "}
                <span
                  className="spinner-border spinner-border-sm ms-2"
                  role="status"
                  aria-hidden="true"
                ></span>
              </button>
              <button className="btn btn-light-success icon-btn" type="button">
                <span
                  className="spinner-border spinner-border-sm"
                  role="status"
                  aria-hidden="true"
                />
                <span className="visually-hidden">Loading...</span>
              </button>
              <button className="btn btn-light-danger icon-btn" type="button">
                <span
                  className="spinner-grow spinner-grow-sm"
                  role="status"
                  aria-hidden="true"
                />
                <span className="visually-hidden">Loading...</span>
              </button>
            </div>
          </Row>
        </CardBody>

        <pre
          className={`loadingbtn mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="loadingbtnexample"
        >
          <code className="language-html">
            {`
<div class="row">
    <div class="col-lg-6 col-12">
        <button class="btn btn-primary d-inline-flex-center" type="button">
            <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
            Loading...
        </button>
        <button class="btn btn-secondary d-inline-flex-center" type="button">
            Wait... <span class="spinner-border spinner-border-sm ms-2" role="status" aria-hidden="true"></span>
        </button>
        <button class="btn btn-success icon-btn" type="button">
            <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            <span class="visually-hidden">Loading...</span>
        </button>
        <button class="btn btn-danger icon-btn" type="button">
            <span class="spinner-grow spinner-grow-sm" role="status" aria-hidden="true"></span>
            <span class="visually-hidden">Loading...</span>
        </button>
    </div>
    <div class="col-lg-6 col-12 mb-3">
        <button class="btn btn-outline-primary d-inline-flex-center" type="button">
            <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
            Loading...
        </button>
        <button class="btn btn-outline-secondary d-inline-flex-center" type="button">
            Wait... <span class="spinner-border spinner-border-sm ms-2" role="status" aria-hidden="true"></span>
        </button>
        <button class="btn btn-outline-success icon-btn" type="button">
            <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            <span class="visually-hidden">Loading...</span>
        </button>
        <button class="btn btn-outline-danger icon-btn" type="button">
            <span class="spinner-grow spinner-grow-sm" role="status" aria-hidden="true"></span>
            <span class="visually-hidden">Loading...</span>
        </button>
    </div>
    <div class="col-lg-6 col-12 mb-3">
        <button class="btn btn-light-primary d-inline-flex-center" type="button">
            <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
            Loading...
        </button>
        <button class="btn btn-light-secondary d-inline-flex-center" type="button">
            Wait... <span class="spinner-border spinner-border-sm ms-2" role="status" aria-hidden="true"></span>
        </button>
        <button class="btn btn-light-success icon-btn" type="button">
            <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            <span class="visually-hidden">Loading...</span>
        </button>
        <button class="btn btn-light-danger icon-btn" type="button">
            <span class="spinner-grow spinner-grow-sm" role="status" aria-hidden="true"></span>
            <span class="visually-hidden">Loading...</span>
        </button>
    </div>
</div>
`}
          </code>
        </pre>
      </Card>
    </Col>
  );
};

export default LoadingButtons;
