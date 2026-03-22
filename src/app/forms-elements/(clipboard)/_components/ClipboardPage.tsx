"use client";
import React, { useRef } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Row,
  Input,
  Container,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconCopy, IconCreditCard, IconCut } from "@tabler/icons-react";

const ClipboardPage: React.FC = () => {
  const textInputRef = useRef<HTMLInputElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const highlightedTextRef = useRef<HTMLSpanElement>(null);
  const paragraphRef = useRef<HTMLParagraphElement>(null);
  const pasteAreaRef = useRef<HTMLTextAreaElement>(null);

  const handleClipboardAction = async (
    action: "copy" | "cut",
    ref: React.RefObject<HTMLElement | HTMLInputElement | HTMLTextAreaElement>,
    isInput: boolean = false
  ) => {
    if (!ref.current) return;

    try {
      let text = "";

      if (isInput) {
        const input = ref.current as HTMLInputElement | HTMLTextAreaElement;
        text = input.value;
        if (action === "cut") input.value = "";
      } else {
        text = ref.current.textContent || ref.current.innerText;
      }

      await navigator.clipboard.writeText(text.trim());
    } catch (err) {
      if (ref.current) {
        const range = document.createRange();
        range.selectNode(ref.current);
        window.getSelection()?.removeAllRanges();
        window.getSelection()?.addRange(range);
        document.execCommand(action);
      }
    }
  };

  // Special handler for highlighted text (only copy)
  const copyHighlightedText = async () => {
    if (!highlightedTextRef.current) return;
    await handleClipboardAction("copy", highlightedTextRef);
  };

  // Special handler for paragraph (only copy)
  const copyParagraph = async () => {
    if (!paragraphRef.current) return;
    await handleClipboardAction("copy", paragraphRef);
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Clipboard"
          title="Forms elements"
          path={["Clipboard"]}
          Icon={IconCreditCard}
        />
        <Row>
          {/* Copy to Text Input */}
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Copy to Text Input</h5>
              </CardHeader>
              <CardBody>
                <div className="bg-light-secondary pa-25 copy-clipboard b-r-10">
                  <Input
                    type="text"
                    innerRef={textInputRef}
                    className="form-control copytext"
                    placeholder="Some text to be copied"
                  />
                  <div className="col-sm-12 mt-4">
                    <Button
                      color="primary"
                      onClick={() =>
                        handleClipboardAction("copy", textInputRef, true)
                      }
                    >
                      <IconCopy size={18} /> <span>Copy Input</span>
                    </Button>{" "}
                    <Button
                      color="danger"
                      onClick={() =>
                        handleClipboardAction("cut", textInputRef, true)
                      }
                    >
                      <IconCut size={18} /> <span>Cut Input</span>
                    </Button>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          {/* Copy to Textarea */}
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Copy to Textarea</h5>
              </CardHeader>
              <CardBody>
                <div className="bg-light-secondary pa-25 copy-clipboard b-r-10">
                  <Input
                    type="textarea"
                    innerRef={textareaRef}
                    className="form-control copytext"
                    rows="3"
                    placeholder="Enter Your Text"
                  />
                  <div className="col-sm-12 mt-4">
                    <Button
                      color="info"
                      onClick={() =>
                        handleClipboardAction("copy", textareaRef, true)
                      }
                    >
                      <IconCopy size={18} /> <span>Copy Input</span>
                    </Button>{" "}
                    <Button
                      color="warning"
                      onClick={() =>
                        handleClipboardAction("cut", textareaRef, true)
                      }
                    >
                      <IconCut size={18} /> <span>Cut Input</span>
                    </Button>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          {/* Copy to Highlighted Text */}
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Copy to Highlighted Text</h5>
              </CardHeader>
              <CardBody>
                <div className="bg-light-secondary pa-25 copy-clipboard b-r-10">
                  <p className="form-control copytext mb-3">
                    For text that you can{" "}
                    <span
                      className="text-bg-primary px-2 b-r-5"
                      ref={highlightedTextRef}
                    >
                      i am going to copy
                    </span>
                  </p>
                  <div>
                    <Button color="success" onClick={copyHighlightedText}>
                      <IconCopy size={18} /> Copy Text
                    </Button>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          {/* Copy to Paragraph */}
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Copy to Paragraph</h5>
              </CardHeader>
              <CardBody>
                <div className="bg-light-secondary pa-25 copy-clipboard b-r-10">
                  <p className="form-control copytext" ref={paragraphRef}>
                    I&#39;d be happy to help you copy a paragraph, but I need a
                    bit more context or the text...
                  </p>
                  <div>
                    <Button
                      color="dark"
                      className="mt-3"
                      onClick={copyParagraph}
                    >
                      <IconCopy size={18} /> Copy Paragraph
                    </Button>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          {/* Paste Area */}
          <Col lg="12">
            <Card className="app-form">
              <CardHeader>
                <h5>Paste</h5>
              </CardHeader>
              <CardBody>
                <div className="b-r-5">
                  <Input
                    type="textarea"
                    innerRef={pasteAreaRef}
                    className="form-control"
                    placeholder="Paste your copied content here"
                    rows="5"
                  />
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ClipboardPage;
