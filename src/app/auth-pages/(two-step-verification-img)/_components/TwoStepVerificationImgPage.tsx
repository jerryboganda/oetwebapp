"use client";
import React, { useRef, ChangeEvent } from "react";
import { Container, Row, Col, Button, Input } from "reactstrap";
import Link from "next/link";
import Image from "next/image";

const TwoStepVerificationImgPage: React.FC = () => {
  const inputsRef = useRef<(HTMLInputElement | HTMLTextAreaElement | null)[]>(
    []
  );

  const digitValidate = (e: ChangeEvent<HTMLInputElement>, index: number) => {
    const newValue = e.target.value.replace(/[^0-9]/g, "");
    e.target.value = newValue;

    if (newValue && index < inputsRef.current.length - 1) {
      inputsRef.current[index + 1]?.focus();
    } else if (!newValue && index > 0) {
      inputsRef.current[index - 1]?.focus();
    }
  };

  return (
    <Container fluid>
      <Row>
        <Col xs="12" className="p-0">
          <div className="login-form-container">
            <div className="mb-4">
              <Link href="/dashboard/ecommerce">
                <Image
                  src="/images/logo/polytronx-dark.svg"
                  width={250}
                  height={65}
                  alt="Logo"
                />
              </Link>
            </div>
            <div className="form_container">
              <form className="app-form rounded-control">
                <Row>
                  <Col xs="12">
                    <div className="mb-5 text-center">
                      <h2 className="text-primary-dark">Verify OTP</h2>
                      <p>
                        Enter the 5 digit code sent to the registered email Id
                      </p>
                    </div>
                  </Col>
                  <Col xs="12">
                    <div className="verification-box d-flex gap-3 mb-3">
                      {[0, 1, 2, 3, 4].map((_, index) => (
                        <Input
                          key={index}
                          type="text"
                          maxLength={1}
                          innerRef={(el) => {
                            inputsRef.current[index] = el;
                          }}
                          className="form-control h-60 w-60 text-center"
                          onInput={(e: ChangeEvent<HTMLInputElement>) =>
                            digitValidate(e, index)
                          }
                        />
                      ))}
                    </div>
                  </Col>
                  <Col xs="12">
                    <p>
                      Did not receive a code?{" "}
                      <Link href="#">
                        <span className="link-primary text-decoration-underline">
                          Resend!
                        </span>
                      </Link>
                    </p>
                  </Col>
                  <Col xs="12">
                    <div className="mb-3">
                      <Button
                        type="submit"
                        color="light-primary"
                        className="w-100"
                      >
                        Verify
                      </Button>
                    </div>
                  </Col>
                </Row>
              </form>
            </div>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default TwoStepVerificationImgPage;
