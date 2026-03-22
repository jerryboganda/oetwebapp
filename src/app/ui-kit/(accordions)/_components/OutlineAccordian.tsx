import React, { useState } from "react";
import {
  Accordion,
  AccordionBody,
  AccordionHeader,
  AccordionItem,
  Card,
  CardHeader,
  CardBody,
  Col,
  UncontrolledCollapse,
} from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import {
  lightAccordionItems,
  outlineAccordionItems,
} from "@/Data/UiKit/AccordionData/accordionPageData";

let DOMPurify: any = null;
if (typeof window !== "undefined") {
  DOMPurify = require("dompurify");
}

const AccordionSection = () => {
  const [openOutline, setOpenOutline] = useState<string>("1");
  const [openLight, setOpenLight] = useState<string>("1");
  const [openFlush, setOpenFlush] = useState<string>("1");

  const toggleOutline = (id: string) =>
    setOpenOutline(openOutline === id ? "" : id);
  const toggleLight = (id: string) => setOpenLight(openLight === id ? "" : id);
  const toggleFlush = (id: string) => setOpenFlush(openFlush === id ? "" : id);

  const sanitize = (html: string) =>
    DOMPurify ? DOMPurify.sanitize(html) : html;

  return (
    <>
      {/* Outline Accordion */}
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Outline Accordion</h5>
            <a id="togglerOutline">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Accordion
              open={openOutline}
              toggle={toggleOutline}
              className="accordion-outline-danger"
            >
              {outlineAccordionItems.map(({ id, title, content }) => {
                const safeHtml = sanitize(
                  `<strong>${content.split(".")[0]}.</strong> ${content
                    .substring(content.indexOf(".") + 1)
                    .trim()}`
                );
                return (
                  <AccordionItem key={`outline-${id}`}>
                    <AccordionHeader targetId={id}>{title}</AccordionHeader>
                    <AccordionBody accordionId={id}>
                      <div dangerouslySetInnerHTML={{ __html: safeHtml }} />
                    </AccordionBody>
                  </AccordionItem>
                );
              })}
            </Accordion>
          </CardBody>
          <UncontrolledCollapse toggler="#togglerOutline">
            <pre>
              <code className="language-html">
                {`<Accordion open={openOutline} toggle={toggleOutline} className="accordion-outline-secondary">
${outlineAccordionItems
  .map(
    ({ id, title, content }) => `  <AccordionItem key="outline-${id}">
    <AccordionHeader targetId="${id}">${title}</AccordionHeader>
    <AccordionBody accordionId="${id}">
      <strong>${content.split(".")[0]}.</strong> ${content
        .substring(content.indexOf(".") + 1)
        .replace(/<code>(.*?)<\/code>/g, "`$1`")
        .trim()}
    </AccordionBody>
  </AccordionItem>`
  )
  .join("\n")}
</Accordion>`}
              </code>
            </pre>
          </UncontrolledCollapse>
        </Card>
      </Col>

      {/* Light Accordion */}
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Light Accordion</h5>
            <a id="togglerLight">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Accordion
              open={openLight}
              toggle={toggleLight}
              className="accordion-light-secondary"
            >
              {lightAccordionItems.map(({ id, title, content }) => {
                const safeHtml = sanitize(
                  `${content.split(".")[0]}. ${content
                    .substring(content.indexOf(".") + 1)
                    .replace(
                      /\.accordion-flush/g,
                      "<code>.accordion-flush</code>"
                    )
                    .trim()}`
                );
                return (
                  <AccordionItem key={`light-${id}`}>
                    <AccordionHeader targetId={id}>{title}</AccordionHeader>
                    <AccordionBody accordionId={id}>
                      <div dangerouslySetInnerHTML={{ __html: safeHtml }} />
                    </AccordionBody>
                  </AccordionItem>
                );
              })}
            </Accordion>
          </CardBody>
          <UncontrolledCollapse toggler="#togglerLight">
            <pre>
              <code className="language-html">
                {`<Accordion open={openLight} toggle={toggleLight} className="accordion-light-secondary">
${lightAccordionItems
  .map(
    ({ id, title, content }) => `  <AccordionItem key="light-${id}">
    <AccordionHeader targetId="${id}">${title}</AccordionHeader>
    <AccordionBody accordionId="${id}">
      ${content}
    </AccordionBody>
  </AccordionItem>`
  )
  .join("\n")}
</Accordion>`}
              </code>
            </pre>
          </UncontrolledCollapse>
        </Card>
      </Col>

      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Flush Accordion (No Border)</h5>
            <a id="togglerFlush">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Accordion
              open={openFlush}
              toggle={toggleFlush}
              className="accordion-flush accordion-secondary"
            >
              {[1, 2, 3].map((num) => (
                <AccordionItem key={`flush-${num}`}>
                  <AccordionHeader targetId={`flush-collapse-${num}`}>
                    Accordion Item #{num}
                  </AccordionHeader>
                  <AccordionBody accordionId={`flush-collapse-${num}`}>
                    Placeholder content for this accordion, which is intended to
                    demonstrate the <code>.accordion-flush</code> class. This is
                    the {num} item&#39;s accordion body.
                  </AccordionBody>
                </AccordionItem>
              ))}
            </Accordion>
          </CardBody>
          <UncontrolledCollapse toggler="#togglerFlush">
            <pre>
              <code className="language-html">
                {`<Accordion open={openFlush} toggle={toggleFlush} className="accordion-flush accordion-secondary">
${[1, 2, 3]
  .map(
    (num) => `  <AccordionItem key="flush-${num}">
    <AccordionHeader targetId="flush-collapse-${num}">
      Accordion Item #${num}
    </AccordionHeader>
    <AccordionBody accordionId="flush-collapse-${num}">
      Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> class. This is the ${num} item's accordion body.
    </AccordionBody>
  </AccordionItem>`
  )
  .join("\n")}
</Accordion>`}
              </code>
            </pre>
          </UncontrolledCollapse>
        </Card>
      </Col>
    </>
  );
};

export default AccordionSection;
