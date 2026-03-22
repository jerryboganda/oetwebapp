import React, { useState } from "react";
import {
  Col,
  Card,
  CardHeader,
  CardBody,
  Accordion,
  AccordionItem,
  AccordionHeader,
  UncontrolledCollapse,
  AccordionBody,
} from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import { accordionItems } from "@/Data/UiKit/AccordionData/accordionPageData";

let DOMPurify: any = null;
if (typeof window !== "undefined") {
  DOMPurify = require("dompurify");
}

const SimpleAccordion: React.FC = () => {
  const [open, setOpen] = useState("1");

  const toggle = (id: string) => {
    setOpen(open === id ? "" : id);
  };

  return (
    <Col md={6}>
      <Card>
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5>Simple Accordion</h5>
          <a href="#" id="togglerAccordion">
            <IconCode className="source cursor-pointer" size={32} />
          </a>
        </CardHeader>

        <CardBody>
          <Accordion
            open={open}
            toggle={toggle}
            className="app-accordion accordion-secondary"
          >
            {accordionItems.map(({ id, title, content }) => {
              const dirtyHTML = `
                <strong>${content.split(".")[0]}.</strong>
                ${content
                  .substring(content.indexOf(".") + 1)
                  .replace(/\.accordion-body/g, `<code>.accordion-body</code>`)
                  .trim()}
              `;

              const safeHTML =
                typeof window !== "undefined" && DOMPurify
                  ? DOMPurify.sanitize(dirtyHTML)
                  : dirtyHTML;

              return (
                <AccordionItem key={id}>
                  <AccordionHeader targetId={id}>{title}</AccordionHeader>
                  <AccordionBody accordionId={id}>
                    <div dangerouslySetInnerHTML={{ __html: safeHTML }} />
                  </AccordionBody>
                </AccordionItem>
              );
            })}
          </Accordion>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerAccordion">
          <pre>
            <code className="language-html">
              {`<Accordion open={open} toggle={toggle} className="app-accordion accordion-secondary">
${accordionItems
  .map(
    ({ id, htmlId, title, content }) => `  <AccordionItem key="${id}">
    <AccordionHeader targetId="${htmlId}">${title}</AccordionHeader>
    <AccordionBody accordionId="${htmlId}">
      <strong>${content.split(".")[0]}.</strong> ${content
        .substring(content.indexOf(".") + 1)
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
  );
};

export default SimpleAccordion;
